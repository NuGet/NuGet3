﻿using NuGet.Versioning;
using System;
using System.Globalization;
using System.Xml;

namespace NuGet.Protocol.Core.v3.DependencyInfo
{
    internal class DependencyInfo
    {
        public string Id { get; set; }
        public VersionRange Range { get; set; }
        public RegistrationInfo RegistrationInfo { get; set; }

        public override string ToString()
        {
            return String.Format(CultureInfo.InvariantCulture, "{0} {1}", Id, Range);
        }
    }
}
