using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using VaderConsulting.Database;
using VaderConsulting.Dependency;
using VaderConsulting.Helper;

namespace VaderConsulting.Orbus
{
    public class iServer : VaderConsulting.DataLayer.DataLayer, VaderConsulting.DataLayer.IDataLayer
    {
        #region Fields

        private string _Version = "233.07"; // Default to the lowest version we understand
        private string _DocumentRoot = "";
        private string _DatabaseConnectionString = "";
        private int _SQLServerQueryTimeout = 30;
        private SQLServer _DependencyDatabase = null;
        private bool _ShowDebugMessages = false;
        private List<BusinessApplication> _Services = null;
        private List<Server> _Servers = null;
        private Dependency.Collection _DependencyCollection = null;

        #region Queries

        private string _GetVersion = "";
        private string _23307_GetDrawingsQuery =
@"SELECT * FROM 
(
SELECT Drawing.ObjectID                              AS DrawingID
     , Drawing.ObjectVersionID                       AS DrawingVersionID
     , Drawing.ObjectName                            AS DrawingName
     , Drawing.ObjectDescription                     AS DrawingDescription
     , Drawing.SystemVersionNo                       AS DrawingVersion
     , PagePreviews.PagePreviewImage                 AS DrawingImage 
     , CASE ISNULL(DrawingStatus.AttributeValue, -1)
          WHEN -1 THEN 'N/A'
          WHEN 0  THEN 'Draft'
          WHEN 1  THEN 'Pending Review'
          WHEN 2  THEN 'Approved'
       ELSE DrawingStatus.AttributeValue
       END                                           AS DrawingStatuse
     , Object.ObjectName                             AS DrawingFocusedServiceName
     , LastDrawingUpdate.AttributeValue              AS LastUpdate
     , Version.Notes                                 AS DrawingNote
     , ROW_NUMBER() OVER (PARTITION BY Drawing.ObjectID ORDER BY Drawing.ObjectID) AS GroupedRowNumber
FROM vwObject Drawing
INNER JOIN RelationDocument  rd                ON rd.DocumentId                 = Drawing.ObjectID
INNER JOIN Relation          Relation          ON Relation.RelationshipId       = rd.RelationshipId
INNER JOIN vwObject          Object            ON Object.ObjectID               = Relation.FromObjectId AND Object.TypeId = #BusinessApplicationID#
INNER JOIN Version           Version           ON Version.ObjectID              = Drawing.ObjectId AND Version.ID = Drawing.ObjectVersionId
LEFT JOIN VisioPagePreviews  PagePreviews      ON PagePreviews.ObjectID         = Drawing.ObjectId AND Version.ID = Drawing.ObjectVersionId
LEFT JOIN AttributeValueText DrawingStatus     ON DrawingStatus.AttributeId     = '#DRAWINGSTATUS#' AND DrawingStatus.VersionId = Drawing.ObjectVersionId
LEFT JOIN AttributeValueText LastDrawingUpdate ON LastDrawingUpdate.AttributeId = '#LASTUPDATE#' AND LastDrawingUpdate.VersionId = Drawing.ObjectVersionId
WHERE Drawing.TypeId = #DRAWINGID# AND Drawing.IsDeleted = 0 AND Drawing.ObjectParentID = '#DOCUMENTROOTID#'
) t
WHERE GroupedRowNumber = 1
ORDER BY DrawingName";

        private string _33400_GetDrawingsQuery =
@"SELECT Drawing.ObjectID                 AS DrawingID
     , Drawing.CurrentVersionID         AS DrawingVersionID
     , Drawing.ObjectName               AS DrawingName
     , Drawing.ObjectDescription        AS DrawingDescription
     , Version.SystemVersionNo          AS DrawingVersion
     , PagePreviews.PagePreviewImage    AS DrawingImage
     , CASE ISNULL(DrawingStatus.ValueText, -1)
          WHEN -1 THEN 'N/A'
          WHEN 0  THEN 'Draft'
          WHEN 1  THEN 'Pending Review'
          WHEN 2  THEN 'Approved'
          ELSE DrawingStatus.ValueText
        END                             AS DrawingStatus
     , DrawingFocusedService.ValueText  AS DrawingFocusedServiceName
     , LastDrawingUpdate.AttributeValue AS LastUpdate
     , Version.Notes
FROM Object Drawing
INNER JOIN vwObject v ON Drawing.ObjectID = v.ObjectID
INNER JOIN (SELECT v.ObjectID
                 , MAX(v.SystemVersionNo) AS SystemVersionNo
                 , o.CurrentVersionId AS ID
            FROM Object AS o 
            INNER JOIN Version AS v ON o.ObjectID = v.ObjectID
            GROUP BY v.ObjectID, o.CurrentVersionId 
           ) AS Version                         ON v.CurrentVersionId                = Version.ID
INNER JOIN VisioPagePreviews PagePreviews       ON PagePreviews.ObjectID             = Version.ObjectId   AND PagePreviews.VersionId            = Version.ID
INNER JOIN AttributeValue DrawingStatus         ON DrawingStatus.AttributeId         = '#DRAWINGSTATUS#'  AND DrawingStatus.VersionId           = Version.ID
INNER JOIN AttributeValue LastDrawingUpdate     ON LastDrawingUpdate.AttributeId     = '#LASTUPDATE#'     AND LastDrawingUpdate.VersionId           = Drawing.ObjectVersionId
INNER JOIN AttributeValue DrawingFocusedService ON DrawingFocusedService.AttributeId = '#FOCUSEDSERVICE#' AND DrawingFocusedService.VersionId   = Version.ID
WHERE v.TypeId           = #DRAWINGID# AND 
      Drawing.DeleteFlag = 0   AND 
      v.ObjectParentID   = '#DOCUMENTROOTID#'
ORDER BY Drawing.ObjectName";

        private string _23307_GetFocusedBusinessApplicationFromDrawingQuery =
@"SELECT ObjectID
     , ObjectName
     , ObjectDescription
     , ObjectVersionId
FROM vwObject v 
WHERE v.ObjectName LIKE '#FOCUSEDSERVICENAME#' 
  AND v.TypeId = #BusinessApplicationID#";

        private string _33400_GetFocusedBusinessApplicationFromDrawingQuery =
@"SELECT ObjectID
     , ObjectName
     , ObjectDescription
     , CurrentVersionId
FROM vwObject v 
WHERE v.ObjectName LIKE '#FOCUSEDSERVICENAME#' 
  AND v.TypeId = #BusinessApplicationID#";

        private string _23307_GetBusinessApplicationDetailsQuery =
@"SELECT DISTINCT Service.ObjectID             AS ServiceID
     , ObjectName                            AS ServiceName
     , ObjectDescription                     AS ServiceDescription
     , ObjectVersionID                       AS ServiceVersionID
     , SystemVersionNo                       AS ServiceVersion
     , Tier.AttributeValue                   AS Tier
     , RunbookOrder.AttributeValue           AS RunbookOrder
     , ServiceDisplayName.AttributeValue     AS ServiceDisplayName
     , ShowInServiceCatalogue.AttributeValue AS ShowInServiceCatalogue
     , SubService.AttributeValue             AS SubService
     , InScope.AttributeValue                AS InScope
     , HighlyAvailable.AttributeValue        AS HighlyAvailable
     , ISNULL(Comment1.AttributeValue,'')    AS Comment1
     , ISNULL(Comment2.AttributeValue,'')    AS Comment2
     , ISNULL(Comment3.AttributeValue,'')    AS Comment3
     , ISNULL(Comment4.AttributeValue,'')    AS Comment4
     , ISNULL(Comment5.AttributeValue,'')    AS Comment5
FROM vwObject Service
INNER JOIN (SELECT ObjectID
                 , MAX(CurrentVersionNo) AS VersionNo
                 , ID AS VersionID 
            FROM dbo.Version AS v GROUP BY ObjectID, ID 
           ) AS Version ON Service.ObjectVersionId = Version.VersionID  
LEFT JOIN AttributeValueText Stream                   ON Stream.VersionId =                 Service.ObjectVersionId AND Stream.AttributeId =                 '#STREAM#'
LEFT JOIN AttributeValueBigInt Tier                   ON Tier.VersionId =                   Service.ObjectVersionId AND Tier.AttributeId =                   '#TIER#'
LEFT JOIN AttributeValueBigInt RunbookOrder           ON RunbookOrder.VersionId =           Service.ObjectVersionId AND RunbookOrder.AttributeId =           '#RUNBOOKORDER#'
LEFT JOIN AttributeValueText ServiceDisplayName       ON ServiceDisplayName.VersionId =     Service.ObjectVersionId AND ServiceDisplayName.AttributeId =     '#SERVICEDISPLAYNAME#'
LEFT JOIN AttributeValueBigInt ShowInServiceCatalogue ON ShowInServiceCatalogue.VersionId = Service.ObjectVersionId AND ShowInServiceCatalogue.AttributeId = '#SHOWINSERVICECATALOG#'
LEFT JOIN AttributeValueBigInt SubService             ON SubService.VersionId =             Service.ObjectVersionId AND SubService.AttributeId =             '#SUBSERVICE#'
LEFT JOIN AttributeValueBigInt InScope                ON InScope.VersionId =                Service.ObjectVersionId AND InScope.AttributeId =                '#INSCOPE#'
LEFT JOIN AttributeValueBigInt HighlyAvailable        ON HighlyAvailable.VersionId =        Service.ObjectVersionId AND HighlyAvailable.AttributeId =        '#HIGHLYAVAILABLE#'
LEFT JOIN AttributeValueText Comment1                 ON Comment1.VersionId =               Service.ObjectVersionId AND Comment1.AttributeId =               '#COMMENT1#'
LEFT JOIN AttributeValueText Comment2                 ON Comment2.VersionId =               Service.ObjectVersionId AND Comment2.AttributeId =               '#COMMENT2#'
LEFT JOIN AttributeValueText Comment3                 ON Comment3.VersionId =               Service.ObjectVersionId AND Comment3.AttributeId =               '#COMMENT3#'
LEFT JOIN AttributeValueText Comment4                 ON Comment4.VersionId =               Service.ObjectVersionId AND Comment4.AttributeId =               '#COMMENT4#'
LEFT JOIN AttributeValueText Comment5                 ON Comment5.VersionId =               Service.ObjectVersionId AND Comment5.AttributeId =               '#COMMENT5#'
WHERE (Service.LibraryId = '00000000-0000-0000-0000-000000000000') 
  AND (Service.IsDeleted = 0) 
  AND (Service.TypeId    = #BusinessApplicationID#) 
  AND (Service.ObjectID  = '#SERVICEID#')";

        private string _33400_GetBusinessApplicationDetailsQuery =
@"SELECT DISTINCT Service.ObjectID             AS ServiceID
     , ObjectName                            AS ServiceName
     , ObjectDescription                     AS ServiceDescription
     , CurrentVersionId                      AS ServiceVersionID
     , Service.SystemVersionNo               AS ServiceVersion
     , Tier.ValueBigInt                      AS Tier
     --, ShowInRunbook.ValueBigInt             AS ShowInRunbook
     , RunbookOrder.ValueBigInt              AS RunbookOrder
     , ServiceDisplayName.ValueText          AS ServiceDisplayName
     , ShowInServiceCatalogue.ValueBigInt    AS ShowInServiceCatalogue
     , SubService.ValueBigInt                AS SubService
     , InScope.ValueBigInt                   AS InScope
FROM vwObject Service
INNER JOIN (SELECT v.ObjectID
                 , MAX(v.SystemVersionNo) AS SystemVersionNo
                 , o.CurrentVersionId AS ID
            FROM Object AS o 
            INNER JOIN Version AS v ON o.ObjectID = v.ObjectID
            GROUP BY v.ObjectID, o.CurrentVersionId 
           ) AS Version                         ON Service.CurrentVersionId =         Version.ID  
LEFT JOIN AttributeValue Stream                 ON Stream.VersionId =                 Service.CurrentVersionId AND Stream.AttributeId =                 '#STREAM#'
LEFT JOIN AttributeValue Tier                   ON Tier.VersionId =                   Service.CurrentVersionId AND Tier.AttributeId =                   '#TIER#'
--LEFT JOIN AttributeValue ShowInRunbook          ON ShowInRunbook.VersionId =          Service.CurrentVersionId AND ShowInRunbook.AttributeId =          '#SHOWINRUNBOOK#'
LEFT JOIN AttributeValue RunbookOrder           ON RunbookOrder.VersionId =           Service.CurrentVersionId AND RunbookOrder.AttributeId =           '#RUNBOOKORDER#'
LEFT JOIN AttributeValue ServiceDisplayName     ON ServiceDisplayName.VersionId =     Service.CurrentVersionId AND ServiceDisplayName.AttributeId =     '#SERVICEDISPLAYNAME#'
LEFT JOIN AttributeValue ShowInServiceCatalogue ON ShowInServiceCatalogue.VersionId = Service.CurrentVersionId AND ShowInServiceCatalogue.AttributeId = '#SHOWINSERVICECATALOG#'
LEFT JOIN AttributeValue SubService             ON SubService.VersionId =             Service.CurrentVersionId AND SubService.AttributeId =             '#SUBSERVICE#'
LEFT JOIN AttributeValue InScope                ON InScope.VersionId =                Service.CurrentVersionId AND InScope.AttributeId =                '#INSCOPE#'
WHERE (Service.LibraryId = '00000000-0000-0000-0000-000000000000') 
  AND (Service.IsDeleted = 0) 
  AND (Service.TypeId    = #BusinessApplicationID#) 
  AND (Service.ObjectID  = '#SERVICEID#')";

        private string _23307_RelationshipsQuery =
@"SELECT r.RelationshipId 
      ,r.FromObjectId 
      ,r.ToObjectId 
      ,o2.ObjectName AS Service 
      ,v.ObjectName AS ComponentName 
      ,r.RelationReason AS Description 
      ,v.TypeId 
      ,v.ObjectDescription AS ComponentDescription 
      ,v.ObjectVersionId AS VersionID 
      --,a.AttributeValue AS ShowInRunbook 
      ,c.AttributeValue AS RunbookOrder
      ,m.AttributeValue AS Mandatory 
FROM dbo.Relation AS r 
LEFT OUTER JOIN dbo.vwObject AS v ON r.ToObjectId = v.ObjectID 
LEFT OUTER JOIN dbo.Object AS o2 ON r.FromObjectId = o2.ObjectID 
LEFT OUTER JOIN RelationType rt ON r.RelationTypeId = rt.RelationTypeID 
LEFT OUTER JOIN Timestamp t ON t.TimestampId = r.TimestampId 
LEFT OUTER JOIN Object o ON r.RelationshipId = o.ObjectId 
--LEFT OUTER JOIN AttributeValueBigInt a ON a.ObjectId = v.ObjectID AND a.AttributeId = '#SHOWINRUNBOOK#' AND a.VersionId = v.ObjectVersionId 
LEFT OUTER JOIN AttributeValueBigInt c ON c.ObjectId = v.ObjectID AND c.AttributeId = '#RUNBOOKORDER#' AND c.VersionId = v.ObjectVersionId 
LEFT OUTER JOIN AttributeValueBigInt m ON m.ObjectId = o.ObjectID AND m.AttributeId = '#MANDATORY#'
WHERE (r.FromObjectId LIKE '#OBJECTID#') AND (r.DeleteFlag = 0)";

        private string _33400_RelationshipsQuery =
@"SELECT r.RelationshipId 
      ,r.FromObjectId 
      ,r.ToObjectId 
      ,o2.ObjectName AS Service 
      ,v.ObjectName AS ComponentName 
      ,r.RelationReason AS Description 
      ,v.TypeId 
      ,v.ObjectDescription AS ComponentDescription 
      ,v.CurrentVersionId AS VersionID 
      --,a.ValueBigInt AS ShowInRunbook 
      ,c.ValueBigInt AS RunbookOrder
      ,m.ValueBigInt AS Mandatory 
FROM dbo.Relation AS r 
LEFT OUTER JOIN dbo.vwObject AS v  ON r.ToObjectId     = v.ObjectID 
LEFT OUTER JOIN dbo.Object   AS o2 ON r.FromObjectId   = o2.ObjectID 
LEFT OUTER JOIN RelationType AS rt ON r.RelationTypeId = rt.RelationTypeID 
LEFT OUTER JOIN Object o ON r.RelationshipId = o.ObjectId 
--LEFT OUTER JOIN AttributeValue a ON a.ObjectId = v.ObjectID AND a.AttributeId = '#SHOWINRUNBOOK#' AND a.VersionId = v.CurrentVersionId 
LEFT OUTER JOIN AttributeValue c ON c.ObjectId = v.ObjectID AND c.AttributeId = '#RUNBOOKORDER#' AND c.VersionId = v.CurrentVersionId 
LEFT OUTER JOIN AttributeValue m ON m.ObjectId = o.ObjectID AND m.AttributeId = '#MANDATORY#'
WHERE (r.FromObjectId LIKE '#OBJECTID#')";

        private string _23307_GetHARelationshipsQuery =
@"SELECT r.RelationshipId 
      ,r.FromObjectId 
      ,r.ToObjectId 
      ,o2.ObjectName AS Server1  
      ,v.ObjectName AS Server2   
      ,v.ObjectVersionId AS VersionID  
      ,m.AttributeValue AS Mandatory 
      ,h.AttributeValue AS HAGroupName
FROM dbo.Relation AS r 
LEFT OUTER JOIN dbo.vwObject AS v ON r.ToObjectId = v.ObjectID  
LEFT OUTER JOIN dbo.Object AS o2 ON r.FromObjectId = o2.ObjectID  
LEFT OUTER JOIN dbo.RelationDocument AS rd ON r.RelationshipId = rd.RelationshipId
LEFT OUTER JOIN RelationType rt ON r.RelationTypeId = rt.RelationTypeID 
LEFT OUTER JOIN Object o3 ON rd.DocumentId = o3.ObjectID
LEFT OUTER JOIN Timestamp t ON t.TimestampId = r.TimestampId  
LEFT OUTER JOIN Object o ON r.RelationshipId = o.ObjectId  
LEFT OUTER JOIN AttributeValueBigInt c ON c.ObjectId = v.ObjectID AND c.AttributeId = '#RUNBOOKORDER#' AND c.VersionId = v.ObjectVersionId 
LEFT OUTER JOIN AttributeValueBigInt m ON m.ObjectId = o.ObjectID AND m.AttributeId = '#MANDATORY#'
LEFT OUTER JOIN AttributeValueText h ON h.ObjectID = o.ObjectID AND h.AttributeID = '#HAGROUPNAME#'
WHERE (r.DeleteFlag = 0) AND rd.DocumentId = '#DRAWINGID#' AND
      (
       r.FromObjectId IN (
                          #INCLUDELIST#
                         )
      )";

        private string _33400_GetHARelationshipsQuery =
@"SELECT r.RelationshipId 
      ,r.FromObjectId 
      ,r.ToObjectId 
      ,o2.ObjectName AS Server1  
      ,v.ObjectName AS Server2   
      ,v.ObjectVersionId AS VersionID  
      ,m.AttributeValue AS Mandatory 
FROM dbo.Relation AS r 
LEFT OUTER JOIN dbo.vwObject AS v ON r.ToObjectId = v.ObjectID  
LEFT OUTER JOIN dbo.Object AS o2 ON r.FromObjectId = o2.ObjectID  
LEFT OUTER JOIN RelationType rt ON r.RelationTypeId = rt.RelationTypeID 
LEFT OUTER JOIN Timestamp t ON t.TimestampId = r.TimestampId  
LEFT OUTER JOIN Object o ON r.RelationshipId = o.ObjectId  
LEFT OUTER JOIN AttributeValueBigInt c ON c.ObjectId = v.ObjectID AND c.AttributeId = '#RUNBOOKORDER#' AND c.VersionId = v.ObjectVersionId 
LEFT OUTER JOIN AttributeValueBigInt m ON m.ObjectId = o.ObjectID AND m.AttributeId = '#MANDATORY#'
WHERE (r.DeleteFlag = 0) AND
      (
       r.FromObjectId IN (
                          #INCLUDELIST#
                         )
      )";

        private string _23307_GetServerDetailsQuery =
@"SELECT n.ObjectID
     , n.Server
     , ISNULL(avt.AttributeValue,'Other') AS Site 
     , ISNULL(Stream.AttributeValue,3)    AS Stream
     , ISNULL(InScope.AttributeValue,0)   AS InScope
     , ISNULL(Virtual.AttributeValue,0)   AS Virtual
     , ISNULL(AddToMP.AttributeValue,0)   AS AddToMP
     , ISNULL(Comment1.AttributeValue,'') AS Comment1
     , ISNULL(Comment2.AttributeValue,'') AS Comment2
     , ISNULL(Comment3.AttributeValue,'') AS Comment3
     , ISNULL(Comment4.AttributeValue,'') AS Comment4
     , ISNULL(Comment5.AttributeValue,'') AS Comment5
FROM dbo.AttributeValueText AS avt 
INNER JOIN ( SELECT DISTINCT o.ObjectID
                           , o.ObjectName        AS Server
                           , o.ObjectDescription AS Description
                           , v.VersionNo
                           , v.VersionID 
                           , o.ObjectVersionId
             FROM dbo.vwObject AS o INNER JOIN ( SELECT ObjectID
                                                      , MAX(CurrentVersionNo) AS VersionNo
                                                      , ID AS VersionID 
                                                 FROM dbo.Version AS v 
                                                 GROUP BY ObjectID, ID ) AS v ON o.ObjectVersionId = v.VersionID 
             WHERE (o.LibraryId = '00000000-0000-0000-0000-000000000000') 
               AND (o.IsDeleted = 0) 
               AND (o.TypeId = #SERVERID#) 
           ) AS n ON avt.AttributeId = '#PHYSICALSITE#' AND avt.ObjectId = n.ObjectID AND n.VersionID = avt.VersionId 
LEFT JOIN AttributeValueText Stream    ON Stream.VersionId   = n.ObjectVersionId AND Stream.AttributeId   = '#STREAM#'
LEFT JOIN AttributeValueBigInt InScope ON InScope.VersionId  = n.ObjectVersionId AND InScope.AttributeId  = '#INSCOPE#'
LEFT JOIN AttributeValueBigInt Virtual ON Virtual.VersionId  = n.ObjectVersionId AND Virtual.AttributeId  = '#VIRTUAL#'
LEFT JOIN AttributeValueBigInt AddToMP ON AddToMP.VersionId  = n.ObjectVersionId AND AddToMP.AttributeId  = '#ADDTOMP#'
LEFT JOIN AttributeValueText Comment1  ON Comment1.VersionId = n.ObjectVersionId AND Comment1.AttributeId = '#COMMENT1#'
LEFT JOIN AttributeValueText Comment2  ON Comment2.VersionId = n.ObjectVersionId AND Comment2.AttributeId = '#COMMENT2#'
LEFT JOIN AttributeValueText Comment3  ON Comment3.VersionId = n.ObjectVersionId AND Comment3.AttributeId = '#COMMENT3#'
LEFT JOIN AttributeValueText Comment4  ON Comment4.VersionId = n.ObjectVersionId AND Comment4.AttributeId = '#COMMENT4#'
LEFT JOIN AttributeValueText Comment5  ON Comment5.VersionId = n.ObjectVersionId AND Comment5.AttributeId = '#COMMENT5#'
ORDER BY SERVER";

        private string _33400_GetServerDetailsQuery =
@"SELECT n.ObjectID
     , n.Server
     , avt.ValueText AS Site 
     , Stream.ValueText AS Stream
     , InScope.ValueBigInt AS InScope
     , Virtual.ValueBigInt AS Virtual
FROM AttributeValue AS avt 
INNER JOIN ( SELECT DISTINCT o.ObjectID
                           , o.ObjectName AS Server
                           , o.ObjectDescription AS Description
                           , v.SystemVersionNo
                           , v.ID 
             FROM dbo.vwObject AS o 
             INNER JOIN (SELECT v.ObjectID
                              , MAX(v.SystemVersionNo) AS SystemVersionNo
                              , o.CurrentVersionId AS ID
                         FROM Object AS o 
                         INNER JOIN Version AS v ON o.ObjectID = v.ObjectID
                         GROUP BY v.ObjectID, o.CurrentVersionId
                        ) AS v ON o.CurrentVersionId = v.ID 
             WHERE (o.LibraryId = '00000000-0000-0000-0000-000000000000') 
               AND (o.IsDeleted = 0) 
               AND (o.TypeId = #SERVERID#) 
           ) AS n ON avt.AttributeId = '#PHYSICALSITE#' AND avt.ObjectId = n.ObjectID AND n.ID = avt.VersionId 
LEFT JOIN AttributeValue Stream ON Stream.VersionId = n.ID AND Stream.AttributeId = '#STREAM#'
LEFT JOIN AttributeValue InScope ON Stream.VersionId = n.ID AND InScope.AttributeId = '#INSCOPE#'
LEFT JOIN AttributeValue Virtual ON Stream.VersionId = n.ID AND Virtual.AttributeId = '#VIRTUAL#'
ORDER BY SERVER";


        #endregion

        #region Attributes

        private string _BusinessApplicationID = "312";
        private string _DrawingID = "571";
        private string _ServerID = "314";
        private string _AddToMPAttributeID = "DBD27C6E-9156-4E0D-8D3C-27EC3A75FDD2";
        //private string _DevelopmentDocumentRootID = "D0C3BFA8-6619-4545-A030-486DFACC0986";
        //private string _ProductionDocumentRootID = "0FCB6CF0-CC74-40B2-8DC6-1067DC216C1E";
        private string _DrawingStatusAttributeID = "C302BC0C-1FB1-4E25-A7AE-AC018FF27190";
        private string _FocusedServiceAttributeID = "CB48F04F-03AA-47A1-BE2C-43F4907438F5";
        private string _HAGroupNameAttributeID = "74C24CBD-4E67-4DEC-A8F0-A9D6E10171BD";
        private string _HighlyAvailableAttributeID = "B08641FE-FD5E-4E06-9D04-495A94F34246";
        private string _InScopeAttributeID = "D29C2600-F53E-4B25-8F4A-818FC7656FAE";
        private string _LastUpdateAttributeID = "E2A4F6ED-5764-46E5-B862-CE8982CD2C35";
        private string _MandatoryAttributeID = "BA7AB41D-B158-4CD2-AA5F-A197C38B17F7";
        private string _PhysicalSiteAttributeID = "DBB1AFF3-8C8B-424E-8B2F-7FD9CE2568DA";
        private string _RunbookOrderAttributeID = "E94D58B7-8942-4A05-92EA-CD5942C49C07";
        private string _ServiceDisplayNameAttributeID = "1AC31119-F69D-440A-88BF-F177D760603F";
        private string _ShowInServiceCatalogAttributeID = "F80FD169-6EC2-47E9-951F-4D8988D6C166";
        private string _StreamAttributeID = "749FB735-AB26-4302-BAAD-7FBDFFAF8A8F";
        private string _SubServiceAttributeID = "FA0F55D9-EE64-4159-88D4-2290C1281979";
        private string _TierAttributeID = "C4E42F57-8BE6-452F-841D-C2B5070CAA1A";
        private string _VirtualAttributeID = "74CB39F9-FC2D-495C-B752-50031FF803B1";
        private string _Comment1ID = "D61B7818-63E2-4533-8940-86C206181175";
        private string _Comment2ID = "C4A79D3B-55F8-4145-866B-DEAC2D6704CC";
        private string _Comment3ID = "67CA0DE7-65C2-4804-AEE3-30B74E6C5899";
        private string _Comment4ID = "34B44AF9-759D-4B3E-AE9B-A80422482836";
        private string _Comment5ID = "A072150C-9ACD-4121-A661-7049C4BAB478";

        #endregion

        #endregion

        #region Constructors

        public iServer(string DocumentRoot)
        {
            _DocumentRoot = DocumentRoot;
        }

        public iServer(string DocumentRoot, string DatabaseConnectionString)
        {
            _DocumentRoot = DocumentRoot;
            _DatabaseConnectionString = DatabaseConnectionString;
        }

        public iServer(string DocumentRoot, string DatabaseConnectionString, int QueryTimeout)
        {
            _DocumentRoot = DocumentRoot;
            _DatabaseConnectionString = DatabaseConnectionString;
            _SQLServerQueryTimeout = QueryTimeout;
        }

        #endregion

        #region Properties

        public string Version
        {
            get
            {
                return _Version;
            }
        }

        public string DocumentRoot
        {
            get
            {
                return _DocumentRoot;
            }

            set
            {
                _DocumentRoot = value;
            }
        }

        public string DatabaseConnectionString
        {
            get
            {
                return _DatabaseConnectionString;
            }
            set
            {
                _DatabaseConnectionString = value;
            }
        }

        public int SQLServerQueryTimeout
        {
            get
            {
                return _SQLServerQueryTimeout;
            }
            set
            {
                _SQLServerQueryTimeout = value;
            }
        }

        public string GetVersionQuery
        {
            get
            {
                return _GetVersion;
            }
            set
            {
                _GetVersion = value;
            }
        }

        public bool ShowDebugMessages
        {
            get
            {
                return _ShowDebugMessages;
            }
            set
            {
                _ShowDebugMessages = value;
            }
        }

        public Dependency.Collection DependencyCollection
        {
            get
            {
                return _DependencyCollection;
            }
            set
            {
                _DependencyCollection = value;
            }
        }

        public List<Server> Servers
        {
            get
            {
                return _Servers;
            }

            set
            {
                _Servers = value;
            }
        }

        public List<BusinessApplication> Services
        {
            get
            {
                return _Services;
            }
            set
            {
                _Services = value;
            }
        }

        #endregion

        #region Public Methods

        public override List<BusinessApplication> GetBusinessApplications()
        {
            List<BusinessApplication> Results = new List<BusinessApplication>();

            Debug.WriteLine("[INF1150] Loading Drawings...", "information");

            Application.DoEvents();

            string DrawingsQuery = "";

            switch (_Version)
            {
                default:
                case "233.07":
                    DrawingsQuery = _23307_GetDrawingsQuery
                                      .Replace("#DOCUMENTROOTID#", _DocumentRoot)
                                      .Replace("#DRAWINGSTATUS#", _DrawingStatusAttributeID)
                                      .Replace("#FOCUSEDSERVICE#", _FocusedServiceAttributeID)
                                      .Replace("#BusinessApplicationID#", _BusinessApplicationID)
                                      .Replace("#SERVERID#", _ServerID)
                                      .Replace("#DRAWINGID#", _DrawingID)
                                      .Replace("#LASTUPDATE#", _LastUpdateAttributeID);
                    break;
                case "334.00":
                    DrawingsQuery = _33400_GetDrawingsQuery
                                      .Replace("#DOCUMENTROOTID#", _DocumentRoot)
                                      .Replace("#DRAWINGSTATUS#", _DrawingStatusAttributeID)
                                      .Replace("#FOCUSEDSERVICE#", _FocusedServiceAttributeID)
                                      .Replace("#BusinessApplicationID#", _BusinessApplicationID)
                                      .Replace("#SERVERID#", _ServerID)
                                      .Replace("#DRAWINGID#", _DrawingID)
                                      .Replace("#LASTUPDATE#", _LastUpdateAttributeID);
                    break;
            }

            DataTable DrawingData = _DependencyDatabase.Execute(DrawingsQuery);

            if (_ShowDebugMessages)
            {
                if (DrawingData != null)
                {
                    Debug.WriteLine("[INF1151] " + DrawingData.Rows.Count + " Drawings found", "information");
                }
                else
                {
                    Debug.WriteLine("[INF1151] 0 Drawings found", "information");
                }
            }
            Application.DoEvents();

            if (DrawingData != null)
            {
                Debug.WriteLine("[INF1152] Loading Drawing data...", "information");
                Application.DoEvents();

                foreach (DataRow DrawingRow in DrawingData.Rows)
                {
                    string DrawingID = DrawingRow[0].ToString();
                    string DrawingName = DrawingRow[2].ToString();
                    int DrawingVersion = Convert.ToInt32(DrawingRow[4].ToString());
                    Global.DrawingStatus DrawingStatus = Global.DrawingStatus.Not_Applicable;

                    #region Extract Drawing Status

                    switch (DrawingRow[6].ToString())
                    {
                        case "N/A":
                            DrawingStatus = Global.DrawingStatus.Not_Applicable;
                            break;
                        case "Draft":
                            DrawingStatus = Global.DrawingStatus.Draft;
                            break;
                        case "Pending Review":
                            DrawingStatus = Global.DrawingStatus.Pending_Review;
                            break;
                        case "To be reviewed":
                            DrawingStatus = Global.DrawingStatus.Pending_Review;
                            break;
                        case "Approved":
                            DrawingStatus = Global.DrawingStatus.Approved;
                            break;
                        default: // else
                            DrawingStatus = Global.DrawingStatus.Not_Applicable;
                            break;
                    }

                    #endregion

                    Drawing Drawing = new Drawing(DrawingID, DrawingName, DrawingVersion, DrawingStatus);

                    Drawing.VersionID = Guid.Parse(DrawingRow[1].ToString());
                    Drawing.Description = DrawingRow.ItemArray[3].ToString();

                    Image DrawingImage = null;
                    if (DrawingRow.ItemArray[5] != System.DBNull.Value)
                    {
                        System.IO.MemoryStream ImageStream = new System.IO.MemoryStream((byte[])DrawingRow.ItemArray[5]);
                        DrawingImage = Bitmap.FromStream(ImageStream);
                    }

                    string FocusedBusinessApplicationName = DrawingRow.ItemArray[7].ToString();

                    Drawing.LastUpdate = DrawingRow.ItemArray[8].ToString();

                    Drawing.Notes = DrawingRow.ItemArray[9].ToString();

                    BusinessApplication BusinessApplication = GetBusinessApplication(FocusedBusinessApplicationName);

                    BusinessApplication.Drawing = Drawing;
                    BusinessApplication.Drawing.Image = DrawingImage;

                    if (BusinessApplication != null)
                    {
                        _Services.Add(BusinessApplication);
                        _DependencyCollection.Services.Add(BusinessApplication);
                    }
                    else
                    {
                        base.RaiseAnnouncement("[ERR1033] " + FocusedBusinessApplicationName + ": A Business Application with this name was not found", null);
                    }
                }

                // TODO:  Use async to spin this off with a separate thread
                GetRelationshipData();

                // TODO:  Use async to spin this off with a separate thread
                GetAdditionalServerData();

                if (_ShowDebugMessages)
                {
                    Debug.WriteLine("[INF1153] " + _DependencyCollection.Services.Count + " Business Applications found", "information");
                }
                Application.DoEvents();

                if (DrawingData.Rows.Count != _DependencyCollection.Services.Count)
                {
                    int InScopeServiceCount = _DependencyCollection.Services.Where(s => s.InScope == true).Count();
                    //base.RaiseAnnouncement("[ERR1034] There was a mismatch between the number of Business Applications and Drawings.  The ratio should be 1:1.  Check that each Drawing has a Focus Drawing Name", null);
                }

            }
            return _Services;
        }

        public void SaveDrawing()
        {
        }

        public void SaveBusinessApplication()
        {
        }

        public void SaveServer()
        {
        }

        public void Initialise()
        {
            _DependencyDatabase = new SQLServer(_SQLServerQueryTimeout);
            _DependencyDatabase.OpenConnection(_DatabaseConnectionString);

            #region Determine Database version

            try
            {
                string DBVersion = _DependencyDatabase.Execute(_GetVersion).Rows[0][0].ToString();

                switch (DBVersion)
                {
                    case "":
                        _Version = "233.07";
                        //if (_ShowDebugMessages)
                        //{
                        //    Debug.WriteLine("Could not determine iServer Database version. Defaulting to 233.07");
                        //}

                        base.RaiseAnnouncement("[ERR1025] Could not determine iServer Database version. Defaulting to 233.07", null);
                        break;
                    case "23307":
                        _Version = "233.07";
                        if (_ShowDebugMessages)
                        {
                            Debug.WriteLine("[INF1154] Connected to iServer Database version 233.07", "information");
                        }
                        break;
                    case "33400":
                        _Version = "334.00";
                        if (_ShowDebugMessages)
                        {
                            Debug.WriteLine("[INF1155] Connected to iServer Database version 334.00", "information");
                        }
                        break;
                    default:
                        string iServerVersion = DBVersion.Substring(0, 3) + "." + DBVersion.Substring(3);
                        _Version = iServerVersion;
                        //if (_ShowDebugMessages)
                        //{
                        //    Debug.WriteLine("Connected to **UNSUPPORTED** iServer Database version " + iServerVersion);
                        //}
                        base.RaiseAnnouncement("[ERR1026] Connected to **UNSUPPORTED** iServer Database version " + iServerVersion, null);
                        DialogResult Result = MessageBox.Show("You are connected to an unsupported version of iServer.  This can not be guaranteed to work.\n\nDo you wish to continue?", "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);

                        if (Result == System.Windows.Forms.DialogResult.No)
                        {
                            Environment.Exit(0);
                        }

                        break;
                }

                switch (_Version)
                {
                    case "":
                        break;
                }
            }
            catch (Exception)
            {
                _Version = "233.07";
                //if (_ShowDebugMessages)
                //{
                //    Debug.WriteLine("[ERR1037] Could not determine iServer Database version. Defaulting to 233.07");
                //}
                base.RaiseAnnouncement("[ERR1037] Could not determine iServer Database version. Defaulting to 233.07", null);
            }

            #endregion

            _Services = new List<BusinessApplication>();
            _Servers = new List<Server>();
            _DependencyCollection = new Dependency.Collection();
        }

        #endregion

        #region Private Methods

        private BusinessApplication GetBusinessApplication(string FocusedBusinessApplicationName)
        {
            string FocusedBusinessApplicationQuery = "";

            switch (_Version)
            {
                default:
                case "233.07":
                    FocusedBusinessApplicationQuery = _23307_GetFocusedBusinessApplicationFromDrawingQuery
                                                 .Replace("#FOCUSEDSERVICENAME#", FocusedBusinessApplicationName)
                                                 .Replace("#BusinessApplicationID#", _BusinessApplicationID)
                                                 .Replace("#SERVERID#", _ServerID)
                                                 .Replace("#DRAWINGID#", _DrawingID);
                    break;
                case "334.00":
                    FocusedBusinessApplicationQuery = _33400_GetFocusedBusinessApplicationFromDrawingQuery
                                                 .Replace("#FOCUSEDSERVICENAME#", FocusedBusinessApplicationName)
                                                 .Replace("#BusinessApplicationID#", _BusinessApplicationID)
                                                 .Replace("#SERVERID#", _ServerID)
                                                 .Replace("#DRAWINGID#", _DrawingID);
                    break;
            }

            DataTable FocusedBusinessApplicationData = _DependencyDatabase.Execute(FocusedBusinessApplicationQuery);

            BusinessApplication BusinessApplication = null;

            if (FocusedBusinessApplicationData == null)
            {
                base.RaiseAnnouncement("[ERR1038] Could not load Business Application Data from SQL Server", null);
                return null;
            }

            if (FocusedBusinessApplicationData.Rows.Count > 0)
            {
                BusinessApplication = GetBusinessApplicationDetails(FocusedBusinessApplicationData.Rows[0]);

                if (_ShowDebugMessages)
                {
                    Debug.WriteLine("..." + BusinessApplication.Name, "information");
                }
            }

            return BusinessApplication;
        }

        private BusinessApplication GetBusinessApplicationDetails(DataRow InputData)
        {
            BusinessApplication BusinessApplication = null;
            string BusinessApplicationDetailsQuery = "";
            DataTable BusinessApplicationDetailsData = null;

            BusinessApplication = new BusinessApplication(InputData.ItemArray[1].ToString());
            BusinessApplication.ID = Guid.Parse(InputData.ItemArray[0].ToString());

            if (InputData.ItemArray[3].ToString().Trim() != "")
            {
                try
                {
                    BusinessApplication.VersionID = Guid.Parse(InputData.ItemArray[3].ToString());
                }
                catch
                { }
            }

            switch (_Version)
            {
                default:
                case "233.07":
                    BusinessApplicationDetailsQuery = _23307_GetBusinessApplicationDetailsQuery
                                         .Replace("#SERVICEID#", BusinessApplication.ID.ToString())
                                         .Replace("#STREAM#", _StreamAttributeID)
                                         .Replace("#TIER#", _TierAttributeID)
                                         .Replace("#RUNBOOKORDER#", _RunbookOrderAttributeID)
                                         .Replace("#SERVICEDISPLAYNAME#", _ServiceDisplayNameAttributeID)
                                         .Replace("#SHOWINSERVICECATALOG#", _ShowInServiceCatalogAttributeID)
                                         .Replace("#SUBSERVICE#", _SubServiceAttributeID)
                                         .Replace("#BusinessApplicationID#", _BusinessApplicationID)
                                         .Replace("#INSCOPE#", _InScopeAttributeID)
                                         .Replace("#HIGHLYAVAILABLE#", _HighlyAvailableAttributeID)
                                         .Replace("#COMMENT1#", _Comment1ID)
                                         .Replace("#COMMENT2#", _Comment2ID)
                                         .Replace("#COMMENT3#", _Comment3ID)
                                         .Replace("#COMMENT4#", _Comment4ID)
                                         .Replace("#COMMENT5#", _Comment5ID)
                                         ;

                    break;
                case "334.00":
                    BusinessApplicationDetailsQuery = _33400_GetBusinessApplicationDetailsQuery
                                         .Replace("#SERVICEID#", BusinessApplication.ID.ToString())
                                         .Replace("#STREAM#", _StreamAttributeID)
                                         .Replace("#TIER#", _TierAttributeID)
                                         .Replace("#RUNBOOKORDER#", _RunbookOrderAttributeID)
                                         .Replace("#SERVICEDISPLAYNAME#", _ServiceDisplayNameAttributeID)
                                         .Replace("#SHOWINSERVICECATALOG#", _ShowInServiceCatalogAttributeID)
                                         .Replace("#SUBSERVICE#", _SubServiceAttributeID)
                                         .Replace("#BusinessApplicationID#", _BusinessApplicationID)
                                         .Replace("#INSCOPE#", _InScopeAttributeID)
                                         .Replace("#HIGHLYAVAILABLE#", _HighlyAvailableAttributeID)
                                         .Replace("#COMMENT1#", _Comment1ID)
                                         .Replace("#COMMENT2#", _Comment2ID)
                                         .Replace("#COMMENT3#", _Comment3ID)
                                         .Replace("#COMMENT4#", _Comment4ID)
                                         .Replace("#COMMENT5#", _Comment5ID)
                                         ;
                    break;
            }


            BusinessApplicationDetailsData = _DependencyDatabase.Execute(BusinessApplicationDetailsQuery);

            if (BusinessApplicationDetailsData.Rows.Count == 1)
            {

                #region Description (ItemArray 2)

                try
                {
                    BusinessApplication.Description = BusinessApplicationDetailsData.Rows[0].ItemArray[2].ToString();
                }
                catch
                {
                    base.RaiseAnnouncement("[ERR1039] " + BusinessApplication.Name + " (Business Application): Could not load data for property 'Description'", BusinessApplication);
                }

                #endregion

                #region Version (ItemArray 4)

                try
                {
                    BusinessApplication.Version = Convert.ToInt32(BusinessApplicationDetailsData.Rows[0].ItemArray[4].ToString());
                }
                catch
                {
                    base.RaiseAnnouncement("[ERR1040] " + BusinessApplication.Name + " (Business Application) : Could not load data for property 'Version'", BusinessApplication);
                }

                #endregion

                #region Extract Tier (ItemArray 5)

                try
                {
                    // Tier
                    switch (BusinessApplicationDetailsData.Rows[0].ItemArray[5].ToString())
                    {
                        case "0":
                            BusinessApplication.Tier = Global.RecoveryTier.Zero;
                            break;
                        case "1":
                            BusinessApplication.Tier = Global.RecoveryTier.One;
                            break;
                        case "2":
                            BusinessApplication.Tier = Global.RecoveryTier.Two;
                            break;
                        case "3":
                            BusinessApplication.Tier = Global.RecoveryTier.Three;
                            break;
                        case "4":
                            BusinessApplication.Tier = Global.RecoveryTier.Four;
                            break;
                        default:
                            BusinessApplication.Tier = Global.RecoveryTier.Undefined;
                            break;
                    }
                }
                catch
                {
                    base.RaiseAnnouncement("[ERR1041] " + BusinessApplication.Name + " (Business Application): Could not load data for property 'Tier'", BusinessApplication);
                }

                #endregion

                #region Runbook Order (ItemArray 6)

                try
                {
                    BusinessApplication.RunbookOrder = Convert.ToInt32(BusinessApplicationDetailsData.Rows[0].ItemArray[6].ToString());
                }
                catch
                {
                    base.RaiseAnnouncement("[ERR1042] " + BusinessApplication.Name + " (Business Application): Could not load data for property 'Runbook Order'", BusinessApplication);
                }

                #endregion

                #region Display Name (ItemArray 7)

                try
                {
                    BusinessApplication.DisplayName = BusinessApplicationDetailsData.Rows[0].ItemArray[7].ToString();
                }
                catch
                {
                    base.RaiseAnnouncement("[ERR1043] " + BusinessApplication.Name + " (Business Application): Could not load data for property 'Display Name'", BusinessApplication);
                }

                #endregion

                #region Show in Service Catalogue (ItemArray 8)

                try
                {
                    switch (BusinessApplicationDetailsData.Rows[0].ItemArray[8].ToString())
                    {
                        case "0":
                            BusinessApplication.ShowInServiceCatalogue = false;
                            break;
                        case "1":
                            BusinessApplication.ShowInServiceCatalogue = true;
                            break;
                    }
                }
                catch
                {
                    base.RaiseAnnouncement("[ERR1044] " + BusinessApplication.Name + " (Business Application): Could not load data for property 'Show In Service Catalogue'", BusinessApplication);
                }

                #endregion

                #region Sub Service (ItemArray 9)

                try
                {
                    switch (BusinessApplicationDetailsData.Rows[0].ItemArray[9].ToString())
                    {
                        case "0":
                            BusinessApplication.SubService = false;
                            break;
                        case "1":
                            BusinessApplication.SubService = true;
                            break;
                    }
                }
                catch
                {
                    base.RaiseAnnouncement("[ERR1045] " + BusinessApplication.Name + " (Business Application): Could not load data for property 'Sub Service'", BusinessApplication);
                }

                #endregion

                #region In Scope (ItemArray 10)

                try
                {
                    switch (BusinessApplicationDetailsData.Rows[0].ItemArray[10].ToString())
                    {
                        case "0":
                            BusinessApplication.InScope = false;
                            break;
                        case "1":
                            BusinessApplication.InScope = true;
                            break;
                    }
                }
                catch
                {
                    base.RaiseAnnouncement("[ERR1046] " + BusinessApplication.Name + " (Business Application): Could not load data for property 'In Scope'", BusinessApplication);
                }

                #endregion

                #region Highly Available (ItemArray 11)

                try
                {
                    switch (BusinessApplicationDetailsData.Rows[0].ItemArray[11].ToString())
                    {
                        case "0":
                            BusinessApplication.HighlyAvailable = false;
                            break;
                        case "1":
                            BusinessApplication.HighlyAvailable = true;
                            break;
                    }
                }
                catch
                {
                    base.RaiseAnnouncement("[ERR1047] " + BusinessApplication.Name + " (Business Application): Could not load data for property 'Highly Available'", BusinessApplication);
                }

                #endregion

                #region Comment 1 (ItemArray 12)

                try
                {
                    BusinessApplication.Comment1 = BusinessApplicationDetailsData.Rows[0].ItemArray[12].ToString().TrimStart('[', 'N', 'o', 'n', 'e', ']'); // [None]
                }
                catch
                {
                    base.RaiseAnnouncement("[ERR1201] " + BusinessApplication.Name + " (Business Application): Could not load data for property 'Comment 1'", BusinessApplication);
                }

                #endregion

                #region Comment 2 (ItemArray 13)

                try
                {
                    BusinessApplication.Comment2 = BusinessApplicationDetailsData.Rows[0].ItemArray[13].ToString().TrimStart('[', 'N', 'o', 'n', 'e', ']'); // [None]
                }
                catch
                {
                    base.RaiseAnnouncement("[ERR1202] " + BusinessApplication.Name + " (Business Application): Could not load data for property 'Comment 2'", BusinessApplication);
                }

                #endregion

                #region Comment 3 (ItemArray 14)

                try
                {
                    BusinessApplication.Comment3 = BusinessApplicationDetailsData.Rows[0].ItemArray[14].ToString().TrimStart('[', 'N', 'o', 'n', 'e', ']'); // [None]
                }
                catch
                {
                    base.RaiseAnnouncement("[ERR1203] " + BusinessApplication.Name + " (Business Application): Could not load data for property 'Comment 3'", BusinessApplication);
                }

                #endregion

                #region Comment 4 (ItemArray 15)

                try
                {
                    BusinessApplication.Comment4 = BusinessApplicationDetailsData.Rows[0].ItemArray[15].ToString().TrimStart('[', 'N', 'o', 'n', 'e', ']'); // [None]
                }
                catch
                {
                    base.RaiseAnnouncement("[ERR1204] " + BusinessApplication.Name + " (Business Application): Could not load data for property 'Comment 4'", BusinessApplication);
                }

                #endregion

                #region Comment 5 (ItemArray 16)

                try
                {
                    BusinessApplication.Comment5 = BusinessApplicationDetailsData.Rows[0].ItemArray[16].ToString().TrimStart('[', 'N', 'o', 'n', 'e', ']'); // [None]
                }
                catch
                {
                    base.RaiseAnnouncement("[ERR1205] " + BusinessApplication.Name + " (Business Application): Could not load data for property 'Comment 5'", BusinessApplication);
                }

                #endregion

            }

            return BusinessApplication;
        }

        private void GetRelationshipData()
        {
            Debug.WriteLine("[INF1156] Loading Relationships...", "information");
            Application.DoEvents();

            for (int i = 0; i < _DependencyCollection.Services.Count; i++)
            {
                BusinessApplication BusinessApplication = _DependencyCollection.Services[i];
                List<Server> Servers = new List<Server>();
                string RelationshipsQuery = "";
                string HARelationshipsQuery = "";

                if (_ShowDebugMessages)
                {
                    Debug.WriteLine("..." + BusinessApplication.Name + "...", "information");
                }

                #region Build queries

                switch (_Version)
                {
                    default:
                    case "233.07":
                        RelationshipsQuery = _23307_RelationshipsQuery
                                            .Replace("#OBJECTID#", BusinessApplication.ID.ToString())
                                            .Replace("#RUNBOOKORDER#", _RunbookOrderAttributeID)
                                            .Replace("#MANDATORY#", _MandatoryAttributeID);

                        HARelationshipsQuery = _23307_GetHARelationshipsQuery
                                              .Replace("#RUNBOOKORDER#", _RunbookOrderAttributeID)
                                              .Replace("#MANDATORY#", _MandatoryAttributeID)
                                              .Replace("#HAGROUPNAME#", _HAGroupNameAttributeID);

                        break;
                    case "334.00":
                        RelationshipsQuery = _33400_RelationshipsQuery
                                            .Replace("#OBJECTID#", BusinessApplication.ID.ToString())
                                            .Replace("#RUNBOOKORDER#", _RunbookOrderAttributeID)
                                            .Replace("#MANDATORY#", _MandatoryAttributeID);

                        HARelationshipsQuery = _33400_GetHARelationshipsQuery
                                              .Replace("#RUNBOOKORDER#", _RunbookOrderAttributeID)
                                              .Replace("#MANDATORY#", _MandatoryAttributeID)
                                              .Replace("#HAGROUPNAME#", _HAGroupNameAttributeID);
                        break;
                }

                #endregion

                #region Determine Relationships for this Service

                DataTable Relationships = _DependencyDatabase.Execute(RelationshipsQuery);

                if (Relationships != null)
                {
                    foreach (DataRow RelationshipRow in Relationships.Rows)
                    {
                        // Before we add this as a valid component, check the relationship is in our valid relationship list
                        // This is necessary as we have different document roots to take into account

                        if (RelationshipRow.ItemArray[6].ToString() == _BusinessApplicationID)  // Business Application - 312
                        {
                            string ComponentBusinessApplicationName = RelationshipRow.ItemArray[4].ToString();
                            string ComponentBusinessApplicationID = RelationshipRow.ItemArray[2].ToString();

                            // Get the Service from our list of Services
                            BusinessApplication ComponentBusinessApplication = Global.GetExistingBusinessApplication(ComponentBusinessApplicationName, _DependencyCollection.Services);

                            if (ComponentBusinessApplication != null)
                            {
                                Relationship Relationship = new Relationship(BusinessApplication, ComponentBusinessApplication);

                                Relationship.ID = Guid.Parse(RelationshipRow.ItemArray[0].ToString());
                                Relationship.FromObjectID = Guid.Parse(RelationshipRow.ItemArray[1].ToString());
                                Relationship.ToObjectID = Guid.Parse(RelationshipRow.ItemArray[2].ToString());
                                Relationship.VersionID = Guid.Parse(RelationshipRow.ItemArray[8].ToString());

                                if (ComponentBusinessApplication.Name == BusinessApplication.Name)
                                {
                                    // A service cannot rely on itself
                                    base.RaiseAnnouncement("[ERR1048] " + ComponentBusinessApplication.Name + " (Business Application) cannot rely on itself!  You must correct the Drawing", ComponentBusinessApplication);

                                    Relationship.SystemMandatory = false;

                                    // Set the state of this drawing to InError
                                    ComponentBusinessApplication.Drawing.InError = true;
                                }
                                else
                                {
                                    Relationship.SystemMandatory = RelationshipRow.ItemArray[10].ToString() == "1";

                                    BusinessApplication.Relationships.Add(Relationship);

                                    if (!_DependencyCollection.AddRelationship(ref Relationship))
                                    {
                                        if (Relationship.SystemMandatory)
                                        {
                                            base.RaiseAnnouncement("[ERR1049] Could not add relationship between " + Relationship.FromBusinessApplication.Name + " (Business Application) and " + Relationship.ToBusinessApplication.Name + " (Business Application). Check for a circular dependency", ComponentBusinessApplication);
                                        }
                                    }

                                    if (Relationship.SystemMandatory && !ComponentBusinessApplication.InScope && BusinessApplication.InScope)
                                    {
                                        base.RaiseAnnouncement("[ERR1050] " + ComponentBusinessApplication.Name + " (Business Application) is not In Scope, but is still a Mandatory Component of " + BusinessApplication.Name + " (Business Application).  The Runbook may not reflect the desired state", ComponentBusinessApplication);
                                    }
                                }
                            }
                            else
                            {
                                // This component service has no corresponding drawing, meaning we didn't previously add it, so add it now
                                string BusinessApplicationDetailsQuery = "";

                                switch (_Version)
                                {
                                    default:
                                    case "233.07":
                                        BusinessApplicationDetailsQuery = _23307_GetBusinessApplicationDetailsQuery
                                                                    .Replace("#SERVICEID#", RelationshipRow.ItemArray[2].ToString())
                                                                    .Replace("#STREAM#", _StreamAttributeID)
                                                                    .Replace("#TIER#", _TierAttributeID)
                                                                    .Replace("#RUNBOOKORDER#", _RunbookOrderAttributeID)
                                                                    .Replace("#SERVICEDISPLAYNAME#", _ServiceDisplayNameAttributeID)
                                                                    .Replace("#SHOWINSERVICECATALOG#", _ShowInServiceCatalogAttributeID)
                                                                    .Replace("#SUBSERVICE#", _SubServiceAttributeID)
                                                                    .Replace("#BusinessApplicationID#", _BusinessApplicationID)
                                                                    .Replace("#SERVERID#", _ServerID)
                                                                    .Replace("#DRAWINGID#", _DrawingID)
                                                                    .Replace("#INSCOPE#", _InScopeAttributeID)
                                                                    .Replace("#HIGHLYAVAILABLE#", _HighlyAvailableAttributeID);
                                        break;
                                    case "334.00":
                                        BusinessApplicationDetailsQuery = _33400_GetBusinessApplicationDetailsQuery
                                                                    .Replace("#SERVICEID#", RelationshipRow.ItemArray[2].ToString())
                                                                    .Replace("#STREAM#", _StreamAttributeID)
                                                                    .Replace("#TIER#", _TierAttributeID)
                                                                    .Replace("#RUNBOOKORDER#", _RunbookOrderAttributeID)
                                                                    .Replace("#SERVICEDISPLAYNAME#", _ServiceDisplayNameAttributeID)
                                                                    .Replace("#SHOWINSERVICECATALOG#", _ShowInServiceCatalogAttributeID)
                                                                    .Replace("#SUBSERVICE#", _SubServiceAttributeID)
                                                                    .Replace("#BusinessApplicationID#", _BusinessApplicationID)
                                                                    .Replace("#SERVERID#", _ServerID)
                                                                    .Replace("#DRAWINGID#", _DrawingID)
                                                                    .Replace("#INSCOPE#", _InScopeAttributeID)
                                                                    .Replace("#HIGHLYAVAILABLE#", _HighlyAvailableAttributeID);
                                        break;
                                }

                                DataTable ComponentBusinessApplicationData = _DependencyDatabase.Execute(BusinessApplicationDetailsQuery);

                                if (ComponentBusinessApplicationData.Rows.Count > 0)
                                {
                                    ComponentBusinessApplication = GetBusinessApplicationDetails(ComponentBusinessApplicationData.Rows[0]);

                                    if (Global.GetExistingBusinessApplication(ComponentBusinessApplication.Name, _DependencyCollection.Services) == null)
                                    {
                                        _DependencyCollection.Services.Add(ComponentBusinessApplication);

                                        if (_ShowDebugMessages)
                                        {
                                            Debug.WriteLine("......[INF1157] Created Business Application: " + ComponentBusinessApplication.Name, "information");
                                        }

                                        // Now that it's been added, create a relationship between it and the Original Service
                                        Relationship Relationship = new Relationship(BusinessApplication, ComponentBusinessApplication);

                                        Relationship.ID = Guid.Parse(RelationshipRow.ItemArray[0].ToString());
                                        Relationship.FromObjectID = Guid.Parse(RelationshipRow.ItemArray[1].ToString());
                                        Relationship.ToObjectID = Guid.Parse(RelationshipRow.ItemArray[2].ToString());
                                        Relationship.VersionID = Guid.Parse(RelationshipRow.ItemArray[8].ToString());
                                        Relationship.SystemMandatory = RelationshipRow.ItemArray[10].ToString() == "1";

                                        if (Relationship.SystemMandatory && !ComponentBusinessApplication.InScope && BusinessApplication.InScope)
                                        {
                                            base.RaiseAnnouncement("......[ERR1051] " + ComponentBusinessApplication.Name + " (Business Application) is not In Scope, but is still a Mandatory Component of " + BusinessApplication.Name + " (Business Application).  The Runbook may not reflect the desired state", ComponentBusinessApplication);
                                        }

                                        BusinessApplication.Relationships.Add(Relationship);

                                        // TODO:  When working on Relationships 'properly', the following line will not be needed,
                                        // but the flow-on effects will need to be fully investigated
                                        if (!_DependencyCollection.AddRelationship(ref Relationship))
                                        {
                                            base.RaiseAnnouncement("......[ERR1052] Could not add relationship between " + Relationship.FromBusinessApplication.Name + " (Business Application) and " + Relationship.ToBusinessApplication.Name + " (Business Application)", Relationship.FromBusinessApplication);
                                        }
                                    }
                                }
                            }
                        }
                        else // Properties.Settings.Default.ServerID (314 = Server)
                        {
                            // Does this Server already exist?
                            Server ComponentServer = new Server(RelationshipRow.ItemArray[4].ToString());
                            Server ExistingServer = Global.GetExistingServer(ComponentServer, _DependencyCollection.Servers);

                            if (ExistingServer == null)
                            {
                                // New Server
                                ExistingServer = ComponentServer;

                                ExistingServer.ID = Guid.Parse(RelationshipRow.ItemArray[2].ToString());
                                ExistingServer.VersionID = Guid.Parse(RelationshipRow.ItemArray[8].ToString());
                            }

                            Servers.Add(ExistingServer);

                            // Before we create the relationship, we have to ensure the Service exists
                            BusinessApplication ExistingBusinessApplication = Global.GetExistingBusinessApplication(BusinessApplication, _DependencyCollection.Services);

                            Relationship Relationship = new Relationship();

                            if (ExistingBusinessApplication == null)
                            {
                                ExistingBusinessApplication = BusinessApplication;
                            }

                            Relationship = new Relationship(ref ExistingBusinessApplication, ref ExistingServer);

                            Relationship.ID = Guid.Parse(RelationshipRow.ItemArray[0].ToString());
                            Relationship.FromObjectID = Guid.Parse(RelationshipRow.ItemArray[1].ToString());
                            Relationship.ToObjectID = Guid.Parse(RelationshipRow.ItemArray[2].ToString());
                            Relationship.VersionID = Guid.Parse(RelationshipRow.ItemArray[8].ToString());
                            Relationship.SystemMandatory = RelationshipRow.ItemArray[10].ToString() == "1";
                            Relationship.ResultantStream = ExistingServer.Stream;

                            if (!_DependencyCollection.AddRelationship(ref Relationship))
                            {
                                base.RaiseAnnouncement("[ERR1053] Could not add relationship between " + Relationship.FromBusinessApplication.Name + " (Business Application) and " + Relationship.ToServer.Name + " (Server)", Relationship.FromBusinessApplication);
                            }
                        }
                    }
                }

                #endregion

                // Servers should now contain the list of each Server on the Drawing

                #region Determine HA Relationships

                // Build the list of Server ID's as an IN clause to a select to determine if there are any server to Server relationships on the drawing
                StringBuilder IncludeList = new StringBuilder();

                if (Servers.Count > 0)
                {
                    foreach (Server Server in Servers)
                    {
                        IncludeList.Append("'");
                        IncludeList.Append(Server.ID);
                        IncludeList.Append("',");
                    }

                    string FinalIncludeList = IncludeList.ToString();
                    FinalIncludeList = FinalIncludeList.Remove(FinalIncludeList.Length - 1, 1);

                    // Finally, replace the query placeholder with this include list
                    HARelationshipsQuery = HARelationshipsQuery
                                          .Replace("#INCLUDELIST#", FinalIncludeList)
                                          .Replace("#DRAWINGID#", BusinessApplication.Drawing.ID);

                    DataTable HARelationshipData = _DependencyDatabase.Execute(HARelationshipsQuery);

                    if (HARelationshipData.Rows.Count > 0)
                    {
                        // Found at least one Server to Server relationship. This indicates a HA relationship
                        if (_ShowDebugMessages)
                        {
                            Debug.WriteLine("......[INF1158] HA relationship(s)...", "information");
                        }

                        foreach (DataRow RelationshipRow in HARelationshipData.Rows)
                        {
                            string Server1Name = RelationshipRow[3].ToString();
                            string Server2Name = RelationshipRow[4].ToString();
                            string HAGroupName = "";

                            if (RelationshipRow.ItemArray.Length == 8)
                            {
                                HAGroupName = RelationshipRow[7].ToString();
                            }
                            else
                            {
                                HAGroupName = BusinessApplication.Name + "_Undefined";
                                base.RaiseAnnouncement("[ERR1054] Your client is missing the updated query for GetHARelationships.  Seek assistance from the DR Co-ordinator.", null);
                            }

                            Server Server1 = Global.GetExistingServer(Server1Name, _DependencyCollection.Servers);
                            Server Server2 = Global.GetExistingServer(Server2Name, _DependencyCollection.Servers);
                            bool Mandatory = (bool)(Convert.ToInt32(RelationshipRow[6]) == 1);

                            // Because the Server has a HA relationship, it's Stream must now be X
                            Server1.Stream = Global.RecoveryStream.None3;
                            Server2.Stream = Global.RecoveryStream.None3;

                            HARelationship HARelationship = new HARelationship(Server1, Server2, BusinessApplication, Mandatory);

                            HARelationship.GroupName = HAGroupName;

                            if (HAGroupName == "-UNDEFINED-")
                            {
                                HAGroupName = BusinessApplication.Name + "_Undefined";
                                //base.RaiseAnnouncement("HA Groupname for " + Server1.Name + " (Server) and " + Server2.Name + " (Server) is undefined.  Please update the Drawing", BusinessApplication);
                            }

                            if (Global.GetExistingHARelationship(HARelationship, BusinessApplication.HARelationships) == null)
                            {
                                // Let everyone know they are participating in the HA relationship
                                Server1.AddHARelationship(HARelationship);
                                Server2.AddHARelationship(HARelationship);

                                BusinessApplication.AddHARelationship(HARelationship);

                                if (_ShowDebugMessages)
                                {
                                    Debug.WriteLine(".........[INF1121] Added HA Relationship between " + Server1.Name + " (Server) and " + Server2.Name + " (Server)", "information");
                                }
                            }
                            else
                            {
                                // This relationship *or* it's equivelent already exists
                                //if (_ShowDebugMessages)
                                //{
                                //    Debug.WriteLine(".........[ERR1028] Redundant HA Relationship found between " + Server1.Name + " (Server) and " + Server2.Name + " (Server)");
                                //}
                                base.RaiseAnnouncement("[ERR1055] Redundant HA Relationship found between " + Server1.Name + " (Server) and " + Server2.Name + " (Server)", BusinessApplication);
                            }
                        }
                    }
                }

                #endregion

            }

            if (_ShowDebugMessages)
            {
                Debug.WriteLine("[INF1159] " + _DependencyCollection.Servers.Count + " Servers found", "information");
            }
            Application.DoEvents();
        }

        private void GetAdditionalServerData()
        {
            // Get Additional Server Details (Physical Site / Stream etc)

            Debug.WriteLine("[INF1160] Querying additional details for each Server", "information");
            Application.DoEvents();

            string ServerDetailsQuery = "";

            switch (_Version)
            {
                default:
                case "233.07":
                    ServerDetailsQuery = _23307_GetServerDetailsQuery
                                            .Replace("#PHYSICALSITE#", _PhysicalSiteAttributeID)
                                            .Replace("#STREAM#", _StreamAttributeID)
                                            .Replace("#BusinessApplicationID#", _BusinessApplicationID)
                                            .Replace("#SERVERID#", _ServerID)
                                            .Replace("#DRAWINGID#", _DrawingID)
                                            .Replace("#INSCOPE#", _InScopeAttributeID)
                                            .Replace("#VIRTUAL#", _VirtualAttributeID)
                                            .Replace("#ADDTOMP#", _AddToMPAttributeID)
                                            .Replace("#COMMENT1#", _Comment1ID)
                                            .Replace("#COMMENT2#", _Comment2ID)
                                            .Replace("#COMMENT3#", _Comment3ID)
                                            .Replace("#COMMENT4#", _Comment4ID)
                                            .Replace("#COMMENT5#", _Comment5ID)
                                            ;
                    break;
                case "334.00":
                    ServerDetailsQuery = _33400_GetServerDetailsQuery
                                            .Replace("#PHYSICALSITE#", _PhysicalSiteAttributeID)
                                            .Replace("#STREAM#", _StreamAttributeID)
                                            .Replace("#BusinessApplicationID#", _BusinessApplicationID)
                                            .Replace("#SERVERID#", _ServerID)
                                            .Replace("#DRAWINGID#", _DrawingID)
                                            .Replace("#INSCOPE#", _InScopeAttributeID)
                                            .Replace("#VIRTUAL#", _VirtualAttributeID)
                                            .Replace("#ADDTOMP#", _AddToMPAttributeID)
                                            .Replace("#COMMENT1#", _Comment1ID)
                                            .Replace("#COMMENT2#", _Comment2ID)
                                            .Replace("#COMMENT3#", _Comment3ID)
                                            .Replace("#COMMENT4#", _Comment4ID)
                                            .Replace("#COMMENT5#", _Comment5ID)
                                            ;
                    break;
            }

            DataTable AdditionalServerData = _DependencyDatabase.Execute(ServerDetailsQuery);

            foreach (DataRow Row in AdditionalServerData.Rows)
            {
                Server TargetServer = Global.GetExistingServer(new Server(Row[1].ToString()), _DependencyCollection.Servers);
                //Note: _DependencyCollection.Servers is a collection of Servers across all Business Applications (i.e. that are already loaded)

                Application.DoEvents();

                if (TargetServer != null) // The Server already existed.  ***** This is the normal state
                {
                    string PhysicalSiteName = "";

                    PhysicalSiteName = Row[2].ToString().Trim();

                    // We have to turn this Sitename into an instance of the PhysicalSite class
                    PhysicalSite Site = Global.GetExistingPhysicalSite(PhysicalSiteName, _DependencyCollection.Sites);

                    if (Site == null)
                    {
                        // We need to create a new site
                        Site = new PhysicalSite(PhysicalSiteName);

                        // For now, hard code some values
                        switch (Site.Name)
                        {
                            case "PDC":
                                Site.Internal = true;
                                Site.SiteType = PhysicalSite.PhysicalSiteType.Protected;
                                break;
                            case "SDC":
                                Site.Internal = true;
                                Site.SiteType = PhysicalSite.PhysicalSiteType.Recovery;
                                break;
                            case "QBE":
                                Site.Internal = true;
                                Site.SiteType = PhysicalSite.PhysicalSiteType.NotProtected;
                                break;
                            case "External":
                                Site.Internal = false;
                                Site.SiteType = PhysicalSite.PhysicalSiteType.Protected;
                                break;
                            default:
                                Site.Internal = false;
                                Site.SiteType = PhysicalSite.PhysicalSiteType.NotProtected;
                                break;
                        }

                        _DependencyCollection.Sites.Add(Site);

                        if (_ShowDebugMessages)
                        {
                            Debug.WriteLine("...[INF1122] Created new Site: " + Site.Name, "information");
                        }
                    }

                    Site.AddServer(TargetServer);

                    // If we haven't already set the Server Stream to None (i.e. X because it is HA), then set it here

                    Global.RecoveryStream InitialStream = Global.RecoveryStream.Undefined;

                    switch (Row[3].ToString())
                    {
                        case "0":
                            InitialStream = Global.RecoveryStream.A;
                            break;
                        case "1":
                            InitialStream = Global.RecoveryStream.B;
                            break;
                        case "2":
                            InitialStream = Global.RecoveryStream.C;
                            break;
                        case "3":
                            InitialStream = Global.RecoveryStream.D;
                            break;
                        case "4":
                            InitialStream = Global.RecoveryStream.E;
                            break;
                        case "5":
                            InitialStream = Global.RecoveryStream.None3;
                            break;
                        default:
                            InitialStream = Global.RecoveryStream.Other;
                            break;
                    }

                    Global.RecoveryStream ResultantStream = Global.RecoveryStream.Undefined;
                    bool ServerIsHA = TargetServer.HARelationships.Count > 0;

                    switch (Site.SiteType)
                    {
                        case PhysicalSite.PhysicalSiteType.Protected:     // e.g. PDC

                            if (Site.Internal)
                            {
                                if (ServerIsHA)
                                {
                                    // Regardless of the drawn Stream, the resultant is X
                                    ResultantStream = Global.RecoveryStream.None3;
                                }
                                else
                                {
                                    // The stream is as defined within the data
                                    ResultantStream = InitialStream;
                                }
                            }
                            else // Protected External site
                            {
                                if (InitialStream != Global.RecoveryStream.E)
                                {
                                    base.RaiseAnnouncement("...[ERR1056] " + TargetServer.Name + " (Server) cannot be set to Stream " + InitialStream.ToString() + " at an External Site", TargetServer);

                                    ResultantStream = Global.RecoveryStream.None1;
                                }
                            }

                            break;
                        case PhysicalSite.PhysicalSiteType.Recovery:      // e.g. SDC

                            // Default to None2
                            ResultantStream = Global.RecoveryStream.None2;

                            // There may be validation errors to report...
                            switch (InitialStream)
                            {
                                case Global.RecoveryStream.A:
                                    base.RaiseAnnouncement("...[ERR1057] " + TargetServer.Name + " (Server) cannot be set to Stream A.  Stream A only applies to Business Applications", TargetServer);
                                    break;
                                case Global.RecoveryStream.B:
                                    base.RaiseAnnouncement("...[ERR1058] " + TargetServer.Name + " (Server) cannot be set to Stream B.  SRM is unavailable to Servers hosted at the Recovery Site", TargetServer);
                                    break;
                                case Global.RecoveryStream.C:
                                    base.RaiseAnnouncement("...[ERR1059] " + TargetServer.Name + " (Server) cannot be set to Stream C.  Restore from Backup is unavailable to Servers hosted at the Recovery Site", TargetServer);
                                    break;
                                case Global.RecoveryStream.D:
                                    base.RaiseAnnouncement("...[ERR1060] " + TargetServer.Name + " (Server) cannot be set to Stream D.  Stream D restoration is unavailable to Servers hosted at the Recovery Site", TargetServer);
                                    break;
                                case Global.RecoveryStream.E:
                                    base.RaiseAnnouncement("...[ERR1061] " + TargetServer.Name + " (Server) cannot be set to Stream E.  Stream E cannot be applied to Servers hosted internally", TargetServer);
                                    break;
                                case Global.RecoveryStream.Other:
                                    base.RaiseAnnouncement("...[ERR1062] " + TargetServer.Name + " (Server) cannot be set to Stream F.  This Recovery Stream is reserved for future use", TargetServer);
                                    break;
                                case Global.RecoveryStream.None3:

                                    break;
                            }

                            break;
                        case PhysicalSite.PhysicalSiteType.NotProtected:  // e.g. QBE

                            if (Site.Internal)
                            {
                                // Regardless of the drawn Stream, the resultant will be none.
                                ResultantStream = Global.RecoveryStream.None1;

                                // There may be validation errors to report...
                                switch (InitialStream)
                                {
                                    case Global.RecoveryStream.A:
                                        base.RaiseAnnouncement("...[ERR1063] " + TargetServer.Name + " (Server) cannot be set to Stream A.  Stream A only applies to Business Applications", TargetServer);
                                        break;
                                    case Global.RecoveryStream.B:
                                        base.RaiseAnnouncement("...[ERR1064] " + TargetServer.Name + " (Server) cannot be set to Stream B.  SRM is unavailable to Servers at this Site", TargetServer);
                                        break;
                                    case Global.RecoveryStream.C:
                                        base.RaiseAnnouncement("...[ERR1065] " + TargetServer.Name + " (Server) cannot be set to Stream C.  Restore from Backup is unavailable to Servers at this Site", TargetServer);
                                        break;
                                    case Global.RecoveryStream.D:
                                        base.RaiseAnnouncement("...[ERR1066] " + TargetServer.Name + " (Server) cannot be set to Stream D.  Stream D restoration is unavailable to Servers at this Site", TargetServer);
                                        break;
                                    case Global.RecoveryStream.E:
                                        base.RaiseAnnouncement("...[ERR1067] " + TargetServer.Name + " (Server) cannot be set to Stream E.  Stream E cannot be applied to Servers hosted internally", TargetServer);
                                        break;
                                    case Global.RecoveryStream.Other:
                                        base.RaiseAnnouncement("...[ERR1068] " + TargetServer.Name + " (Server) cannot be set to Stream F.  This Recovery Stream is reserved for future use", TargetServer);
                                        break;
                                    case Global.RecoveryStream.None3:

                                        break;
                                }
                            }
                            else // External, not protected
                            {
                                // Regardless of the drawn Stream, the resultant will be none.
                                ResultantStream = Global.RecoveryStream.None1;

                                // There may be validation errors to report...
                                switch (InitialStream)
                                {
                                    case Global.RecoveryStream.A:
                                        base.RaiseAnnouncement("...[ERR1069] " + TargetServer.Name + " (Server) cannot be set to Stream A.  Stream A only applies to Business Applications", TargetServer);
                                        break;
                                    case Global.RecoveryStream.B:
                                        base.RaiseAnnouncement("...[ERR1070] " + TargetServer.Name + " (Server) cannot be set to Stream B.  SRM is unavailable to Servers at this Site", TargetServer);
                                        break;
                                    case Global.RecoveryStream.C:
                                        base.RaiseAnnouncement("...[ERR1071] " + TargetServer.Name + " (Server) cannot be set to Stream C.  Restore from Backup is unavailable to Servers at this Site", TargetServer);
                                        break;
                                    case Global.RecoveryStream.D:
                                        base.RaiseAnnouncement("...[ERR1072] " + TargetServer.Name + " (Server) cannot be set to Stream D.  Stream D restoration is unavailable to Servers at this Site", TargetServer);
                                        break;
                                    case Global.RecoveryStream.E:
                                        base.RaiseAnnouncement("...[ERR1073] " + TargetServer.Name + " (Server) cannot be set to Stream E.  Stream E cannot be applied to Servers at unprotected Sites", TargetServer);
                                        break;
                                    case Global.RecoveryStream.Other:
                                        base.RaiseAnnouncement("...[ERR1074] " + TargetServer.Name + " (Server) cannot be set to Stream F.  This Recovery Stream is reserved for future use", TargetServer);
                                        break;
                                    case Global.RecoveryStream.None3:
                                        break;
                                }
                            }

                            break;
                    }

                    TargetServer.Stream = ResultantStream;

                    if (TargetServer.PhysicalSite.Name == "Unspecified")
                    {
                        TargetServer.SetSystemHealthState(Global.HealthState.Degraded, "Physical Sitename is Unspecified");

                        if (_ShowDebugMessages)
                        {
                            base.RaiseAnnouncement("...[ERR1075] " + TargetServer.Name + " (Server) has no Physical Sitename", TargetServer);
                        }
                    }
                    else
                    {
                        TargetServer.SetSystemHealthState(Global.HealthState.OK, "");
                    }

                    //if (TargetServer.Name == "HOSTNAME")
                    //{
                    //    int i = 1;
                    //}

                    #region Virtual

                    try
                    {
                        switch (Row[5].ToString())
                        {
                            case "0":
                                TargetServer.Virtual = false;
                                break;
                            case "1":
                                TargetServer.Virtual = true;
                                break;
                        }
                    }
                    catch
                    {
                        base.RaiseAnnouncement("...[ERR1076] " + TargetServer.Name + " (Server): Could not load data for property 'Virtual'", TargetServer);
                    }

                    #endregion

                    #region AddToMP

                    try
                    {
                        switch (Row[6].ToString())
                        {
                            case "0":
                                TargetServer.AddToManagementPack = false;
                                break;
                            case "1":
                                TargetServer.AddToManagementPack = true;
                                break;
                        }
                    }
                    catch
                    {
                        base.RaiseAnnouncement("...[ERR1077] " + TargetServer.Name + " (Server): Could not load data for property 'Add to Management Pack'", TargetServer);
                    }

                    #endregion

                    #region Comment 1

                    try
                    {
                        TargetServer.Comment1 = Row[7].ToString();
                    }
                    catch
                    {
                        base.RaiseAnnouncement("...[ERR1206] " + TargetServer.Name + " (Server): Could not load data for property 'Comment 1'", TargetServer);
                    }

                    #endregion

                    #region Comment 2

                    try
                    {
                        TargetServer.Comment2 = Row[8].ToString();
                    }
                    catch
                    {
                        base.RaiseAnnouncement("...[ERR1207] " + TargetServer.Name + " (Server): Could not load data for property 'Comment 2'", TargetServer);
                    }

                    #endregion

                    #region Comment 3

                    try
                    {
                        TargetServer.Comment3 = Row[9].ToString();
                    }
                    catch
                    {
                        base.RaiseAnnouncement("...[ERR1208] " + TargetServer.Name + " (Server): Could not load data for property 'Comment 3'", TargetServer);
                    }

                    #endregion

                    #region Comment 4

                    try
                    {
                        TargetServer.Comment4 = Row[10].ToString();
                    }
                    catch
                    {
                        base.RaiseAnnouncement("...[ERR1209] " + TargetServer.Name + " (Server): Could not load data for property 'Comment 4'", TargetServer);
                    }

                    #endregion

                    #region Comment 5

                    try
                    {
                        TargetServer.Comment5 = Row[11].ToString();
                    }
                    catch
                    {
                        base.RaiseAnnouncement("...[ERR1210] " + TargetServer.Name + " (Server): Could not load data for property 'Comment 5'", TargetServer);
                    }

                    #endregion
                }
                else
                {
                    // The Server is in the iServer Database, but not contained within any of the listed Services
                    if (_ShowDebugMessages)
                    {
                        base.RaiseAnnouncement("...[ERR1123] " + Row[1].ToString() + " (Server) is not associated with any Service", TargetServer);
                    }
                }
            }
        }

        #endregion

    }
}
