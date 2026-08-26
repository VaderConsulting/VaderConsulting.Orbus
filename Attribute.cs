using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VaderConsulting.Orbus
{
    public class Attribute
    {
        private string _Name = "";
        private string _Tag = "";
        private string _Value = "";

        public Attribute()
        {
        }

        public Attribute(string Name, string Value, string Tag)
        {
            _Name = Name;
            _Value = Value;
            _Tag = Tag;
        }

        public override string ToString()
        {
            return _Name + "= " + _Value;
        }

        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                _Name = value;
            }
        }

        public string Tag
        {
            get
            {
                return _Tag;
            }
            set
            {
                _Tag = value;
            }
        }

        public string Value
        {
            get
            {
                return _Value;
            }
            set
            {
                _Value = value;
            }
        }

    }
}
