﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Jhu.Graywulf.Web.Services
{
    public class TestJsonXmlFormatAttribute : DynamicFormatAttribute
    {
        public TestJsonXmlFormatAttribute()
            : base(typeof(TextResponseMessageFormatter))
        {
        }
    }
}
