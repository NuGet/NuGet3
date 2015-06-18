﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace NuGet.Packaging.Core
{
    public class PackageType
    {
        private static readonly PackageType _defaultType = new PackageType("LegacyConventions", version: new Version(0, 0));

        public PackageType(string name, Version version)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException(Strings.StringCannotBeNullOrEmpty, nameof(name));
            }

            if (version == null)
            {
                throw new ArgumentNullException(nameof(version));
            }

            Name = name;
            Version = version;
        }

        public static PackageType Default => _defaultType;

        public string Name { get; }

        public Version Version { get; }
    }
}
