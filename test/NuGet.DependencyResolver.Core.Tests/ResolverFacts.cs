﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Versioning;
using Xunit;

namespace NuGet.DependencyResolver.Core.Tests
{
    // This project can output the Class library as a NuGet Package.
    // To enable this option, right-click on the project and select the Properties menu item. In the Build tab select "Produce outputs on build".
    public class ResolverFacts
    {
        [Fact]
        public void FasterProviderReturnsResultsBeforeSlowOnesIfExactMatchFound()
        {
            // A 
            var slowProvider = new TestProvider(TimeSpan.FromSeconds(2));
            slowProvider.AddLibrary(new LibraryIdentity
            {
                Name = "A",
                Version = new NuGetVersion("1.0.0")
            });

            var fastProvider = new TestProvider(TimeSpan.Zero);
            fastProvider.AddLibrary(new LibraryIdentity
            {
                Name = "A",
                Version = new NuGetVersion("1.0.0")
            });

            var context = new RemoteWalkContext();
            context.RemoteLibraryProviders.Add(slowProvider);
            context.RemoteLibraryProviders.Add(fastProvider);

            var walker = new RemoteDependencyWalker(context);
            var result = walker.Walk(new LibraryRange
            {
                Name = "A",
                VersionRange = VersionRange.Parse("1.0.0"),
            },
            NuGetFramework.Parse("net45"),
            runtimeIdentifier: null,
            runtimeGraph: null).Result;

            Assert.NotNull(result.Item.Data.Match);
            Assert.NotNull(result.Item.Data.Match.Library);
            Assert.Equal(fastProvider, result.Item.Data.Match.Provider);
        }

        [Fact]
        public void SlowerFeedWinsIfBetterMatchExists()
        {
            // A 
            var slowProvider = new TestProvider(TimeSpan.FromSeconds(2));
            slowProvider.AddLibrary(new LibraryIdentity
            {
                Name = "A",
                Version = new NuGetVersion("1.0.0")
            });

            var fastProvider = new TestProvider(TimeSpan.Zero);
            fastProvider.AddLibrary(new LibraryIdentity
            {
                Name = "A",
                Version = new NuGetVersion("1.1.0")
            });

            var context = new RemoteWalkContext();
            context.RemoteLibraryProviders.Add(slowProvider);
            context.RemoteLibraryProviders.Add(fastProvider);

            var walker = new RemoteDependencyWalker(context);
            var result = walker.Walk(new LibraryRange
            {
                Name = "A",
                VersionRange = VersionRange.Parse("1.0.0"),
            },
            NuGetFramework.Parse("net45"),
            runtimeIdentifier: null,
            runtimeGraph: null).Result;

            Assert.NotNull(result.Item.Data.Match);
            Assert.NotNull(result.Item.Data.Match.Library);
            Assert.Equal(slowProvider, result.Item.Data.Match.Provider);
        }

        public class TestProvider : IRemoteDependencyProvider
        {
            private readonly TimeSpan _delay;
            private readonly List<LibraryIdentity> _libraries = new List<LibraryIdentity>();

            public TestProvider(TimeSpan delay)
            {
                _delay = delay;
            }

            public void AddLibrary(LibraryIdentity identity)
            {
                _libraries.Add(identity);
            }

            public bool IsHttp => true;

            public Task CopyToAsync(LibraryIdentity match, Stream stream, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public async Task<LibraryIdentity> FindLibraryAsync(LibraryRange libraryRange, NuGetFramework targetFramework, CancellationToken cancellationToken)
            {
                if (_delay != TimeSpan.Zero)
                {
                    await Task.Delay(_delay);
                }

                return _libraries.FindBestMatch(libraryRange.VersionRange, l => l?.Version);
            }

            public Task<IEnumerable<LibraryDependency>> GetDependenciesAsync(LibraryIdentity match, NuGetFramework targetFramework, CancellationToken cancellationToken)
            {
                return Task.FromResult(Enumerable.Empty<LibraryDependency>());
            }
        }
    }
}
