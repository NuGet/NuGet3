﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;

namespace NuGet.Protocol.Core.Types
{
    /// <summary>
    /// Cache control settings for the V3 disk cache.
    /// </summary>
    public class SourceCacheContext
    {
        /// <summary>
        /// Default amount of time to cache version lists.
        /// </summary>
        private static readonly TimeSpan DefaultCacheAgeLimitList = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Default amount of time to cache nupkgs.
        /// </summary>
        private static readonly TimeSpan DefaultCacheAgeLimitNupkg = TimeSpan.FromHours(24);

        /// <summary>
        /// If set, ignore the disk cache when listing and downloading packages
        /// </summary>
        public bool NoCache { get; set; }

        /// <summary>
        /// Package version lists from the server older than this date
        /// will be fetched from the server.
        /// </summary>
        /// <remarks>This will be ignored if <see cref="NoCache"/> is true.</remarks>
        /// <remarks>If the value is null the default expiration will be used.</remarks>
        public DateTimeOffset? ListMaxAge { get; set; }

        /// <summary>
        /// Nupkgs from the server older than this date will be fetched from the server.
        /// </summary>
        /// <remarks>This will be ignored if <see cref="NoCache"/> is true.</remarks>
        /// <remarks>If the value is null the default expiration will be used.</remarks>
        public DateTimeOffset? NupkgMaxAge { get; set; }


        /// <summary>
        /// Package version lists from the server older than this time span
        /// will be fetched from the server.
        /// </summary>
        public TimeSpan ListMaxAgeTimeSpan
        {
            get
            {
                return GetCacheTime(ListMaxAge, DefaultCacheAgeLimitList);
            }
        }

        /// <summary>
        /// Packages from the server older than this time span
        /// will be fetched from the server.
        /// </summary>
        public TimeSpan NupkgMaxAgeTimeSpan
        {
            get
            {
                return GetCacheTime(ListMaxAge, DefaultCacheAgeLimitNupkg);
            }
        }

        private TimeSpan GetCacheTime(DateTimeOffset? maxAge, TimeSpan defaultTime)
        {
            var timeSpan = TimeSpan.Zero;

            if (!NoCache)
            {
                // Default
                timeSpan = defaultTime;

                // If the max age is set use that instead of the default
                if (ListMaxAge.HasValue)
                {
                    var difference = DateTimeOffset.UtcNow.Subtract(ListMaxAge.Value);

                    Debug.Assert(difference >= TimeSpan.Zero, "Invalid cache time");

                    if (difference >= TimeSpan.Zero)
                    {
                        timeSpan = difference;
                    }
                }
            }

            return timeSpan;
        }
    }
}
