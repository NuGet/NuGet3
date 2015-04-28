﻿using System;
using System.Collections.Generic;
using NuGet.Protocol.Core.Types;
using NuGet.Protocol.Core.v3.LocalRepositories;
using NuGet.Protocol.Core.v3.RemoteRepositories;

namespace NuGet.Protocol.Core.v3
{
    public static class FactoryExtensionsV2
    {
        public static SourceRepository GetCoreV3(this Repository.RepositoryFactory factory, string source)
        {
            return Repository.CreateSource(Repository.Provider.GetCoreV3(), source);
        }

        public static SourceRepository GetCoreV2(this Repository.RepositoryFactory factory, Configuration.PackageSource source)
        {
            return Repository.CreateSource(Repository.Provider.GetCoreV3(), source);
        }

        public static IEnumerable<Lazy<INuGetResourceProvider>> GetCoreV3(this Repository.ProviderFactory factory)
        {
            yield return new Lazy<INuGetResourceProvider>(() => new DependencyInfoResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new DownloadResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new HttpHandlerResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new MetadataResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new RawSearchResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new RegistrationResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new ReportAbuseResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new SearchLatestResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new ServiceIndexResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new SimpleSearchResourceV3Provider());
            yield return new Lazy<INuGetResourceProvider>(() => new LocalV2FindPackageByIdResourceProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new LocalV3FindPackageByIdResourceProvider());
            yield return new Lazy<INuGetResourceProvider>(() => new RemoteV2FindPackageByIdResourceProvider());

            yield break;
        }
    }
}