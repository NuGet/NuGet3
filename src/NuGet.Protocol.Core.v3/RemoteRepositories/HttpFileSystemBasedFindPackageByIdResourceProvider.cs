﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NuGet.Protocol.Core.Types;

namespace NuGet.Protocol.Core.v3.RemoteRepositories
{
    public class HttpFileSystemBasedFindPackageByIdResourceProvider : ResourceProvider
    {
        private const string HttpFileSystemIndexType = "PackageBaseAddress/3.0.0";

        public HttpFileSystemBasedFindPackageByIdResourceProvider()
            : base(typeof(FindPackageByIdResource),
                   nameof(HttpFileSystemBasedFindPackageByIdResourceProvider), 
                   before: nameof(RemoteV3FindPackagePackageByIdResourceProvider))
        {
        }

        public override async Task<Tuple<bool, INuGetResource>> TryCreate(SourceRepository sourceRepository, CancellationToken token)
        {
            INuGetResource resource = null;
            var serviceIndexResource = await sourceRepository.GetResourceAsync<ServiceIndexResourceV3>();
            var packageBaseAddress = serviceIndexResource?[HttpFileSystemIndexType];

            if (packageBaseAddress != null && packageBaseAddress.Count > 0)
            {
                resource = new HttpFileSystemBasedFindPackageByIdResource(packageBaseAddress);
            }

            return Tuple.Create(resource != null, resource);
        }
    }
}