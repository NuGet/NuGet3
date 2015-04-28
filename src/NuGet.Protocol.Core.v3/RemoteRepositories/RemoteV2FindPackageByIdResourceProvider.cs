﻿using System;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Protocol.Core.Types;
using NuGet.Protocol.Core.v3.LocalRepositories;

namespace NuGet.Protocol.Core.v3.RemoteRepositories
{
    /// <summary>
    /// A <see cref="ResourceProvider"/> for <see cref="FindPackageByIdResource"/> over v2 NuGet feeds.
    /// </summary>
    public class RemoteV2FindPackageByIdResourceProvider : ResourceProvider
    {
        public RemoteV2FindPackageByIdResourceProvider()
            : base(typeof(FindPackageByIdResource), name: nameof(RemoteV2FindPackageByIdResourceProvider), before: nameof(LocalV2FindPackageByIdResourceProvider))
        {
        }

        public override Task<Tuple<bool, INuGetResource>> TryCreate(SourceRepository sourceRepository, CancellationToken token)
        {
            INuGetResource resource = null;

            if (sourceRepository.PackageSource.IsHttp &&
                !sourceRepository.PackageSource.Source.EndsWith("json", StringComparison.OrdinalIgnoreCase))
            {
                resource = new RemoteV2FindPackageByIdResourcce(sourceRepository.PackageSource);
            }

            return Task.FromResult(Tuple.Create(resource != null, resource));
        }
    }
}
