using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HelideckVer2.Models
{
    public class Tag
    {
        public string Name { get; }
        public double Value { get; private set; }

        public Tag(string name)
        {
            Name = name;
        }

        public void Update(double value)
        {
            Value = value;
        }
    }
}
