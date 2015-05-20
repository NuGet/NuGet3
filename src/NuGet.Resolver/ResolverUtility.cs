﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace NuGet.Resolver
{
    public static class ResolverUtility
    {
        /// <summary>
        /// Create an error message to describe the primary issue in an invalid solution.
        /// </summary>
        /// <param name="solution">A partial solution from the resolver</param>
        /// <param name="availablePackages">all packages that were available for the solution</param>
        /// <param name="packagesConfig">packages already installed in the project</param>
        /// <param name="newPackageIds">new packages that are not already installed</param>
        /// <returns>A user friendly diagonstic message</returns>
        public static string GetDiagnosticMessage(IEnumerable<ResolverPackage> solution,
            IEnumerable<PackageDependencyInfo> availablePackages,
            IEnumerable<PackageReference> packagesConfig,
            IEnumerable<string> newPackageIds)
        {
            // remove empty and absent packages, absent packages cannot have error messages
            solution = solution.Where(package => package != null && !package.Absent);

            var allPackageIds = new HashSet<string>(solution.Select(package => package.Id), StringComparer.OrdinalIgnoreCase);
            var newPackageIdSet = new HashSet<string>(newPackageIds, StringComparer.OrdinalIgnoreCase);
            var installedPackageIds = new HashSet<string>(packagesConfig.Select(package => package.PackageIdentity.Id), StringComparer.OrdinalIgnoreCase);

            var requiredPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            requiredPackageIds.UnionWith(newPackageIdSet);
            requiredPackageIds.UnionWith(installedPackageIds);
            var requiredPackages = solution.Where(package => requiredPackageIds.Contains(package.Id)).ToList();

            // all new packages that are not already installed, and that aren't the primary target
            var newDependencyPackageIds = new HashSet<string>(allPackageIds.Except(requiredPackageIds), StringComparer.OrdinalIgnoreCase);

            // 1. find cases where the target package does not satisfy the dependency constraints
            foreach (var targetId in newPackageIdSet.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                var brokenPackage = GetPackagesWithBrokenDependenciesOnId(targetId, requiredPackages)
                    .OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (brokenPackage != null)
                {
                    return GetErrorMessage(targetId, solution, availablePackages, packagesConfig);
                }
            }

            // 2. find cases where the target package is missing dependencies
            foreach (var targetPackage in solution.Where(package => newPackageIdSet.Contains(package.Id))
                .OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase))
            {
                var brokenDependency = GetBrokenDependencies(targetPackage, solution)
                    .OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (brokenDependency != null)
                {
                    return GetErrorMessage(brokenDependency.Id, solution, availablePackages, packagesConfig);
                }
            }

            // 3. find cases where an already installed package is missing a dependency
            // this may happen if an installed package was upgraded by the resolver
            foreach (var targetPackage in solution.Where(package => installedPackageIds.Contains(package.Id))
                .OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase))
            {
                var brokenDependency = GetBrokenDependencies(targetPackage, solution)
                    .OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (brokenDependency != null)
                {
                    return GetErrorMessage(brokenDependency.Id, solution, availablePackages, packagesConfig);
                }
            }

            // 4. find cases where a new dependency has a missing dependency
            // to get the most useful error here, sort the packages by their distance from a required package
            foreach (var targetPackage in solution.Where(package => newDependencyPackageIds.Contains(package.Id))
                    .OrderBy(package => GetLowestDistanceFromTarget(package.Id, requiredPackageIds, solution))
                    .ThenBy(package => package.Id, StringComparer.OrdinalIgnoreCase))
            {
                var brokenDependency = GetBrokenDependencies(targetPackage, solution)
                    .OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (brokenDependency != null)
                {
                    return GetErrorMessage(brokenDependency.Id, solution, availablePackages, packagesConfig);
                }
            }

            // this should only get hit if the inputs are invalid, or the solution has no problems
            return Strings.NoSolution;
        }

        private static string GetErrorMessage(string problemPackageId, IEnumerable<ResolverPackage> solution,
            IEnumerable<PackageDependencyInfo> availablePackages,
            IEnumerable<PackageReference> packagesConfig)
        {
            var message = new StringBuilder();

            // List the package that has an issue, and all packages dependant on the package.
            var dependantPackages = solution.Where(package => package.FindDependencyRange(problemPackageId) != null)
                .Select(package => FormatDependencyConstraint(package, problemPackageId))
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);

            // find the packages config entry if it exists
            var configEntry = packagesConfig.FirstOrDefault(entry => StringComparer.OrdinalIgnoreCase.Equals(entry.PackageIdentity.Id, problemPackageId));

            // If the package does not exist at all, or no dependant packages were found use a simple error message for the problemPackageId
            if (!availablePackages.Any(package => StringComparer.OrdinalIgnoreCase.Equals(problemPackageId, package.Id)) ||
                !dependantPackages.Any())
            {
                message.AppendFormat(CultureInfo.CurrentCulture, Strings.UnableToResolveDependency, problemPackageId);
            }
            else
            {
                var packageOptions = availablePackages.Where(package =>
                    package.Version != null && StringComparer.OrdinalIgnoreCase.Equals(package.Id, problemPackageId));

                // If there was only 1 option for the package, give the version in the error message
                // Packages will allowed versions have already pruned out the disallowed versions,
                // for these packages we should not show the exact version.
                if (packageOptions.Count() == 1 && (configEntry == null || !configEntry.HasAllowedVersions))
                {
                    var problemPackageString = String.Format(CultureInfo.InvariantCulture, "{0} {1}",
                        problemPackageId, packageOptions.First().Version.ToString());

                    // Return an error with the problem package id and version, and all parent packages that might have caused the issue
                    message.AppendFormat(CultureInfo.CurrentCulture, Strings.VersionIsNotCompatible, problemPackageString, String.Join(", ", dependantPackages));
                }
                else
                {
                    // Return an error with the problem package id, and all parent packages that might have caused the issue
                    message.AppendFormat(CultureInfo.CurrentCulture, Strings.UnableToFindCompatibleVersion, problemPackageId, String.Join(", ", dependantPackages));
                }
            }

            // if packages.config has additional constraints, append them to the message
            if (configEntry != null && configEntry.HasAllowedVersions)
            {
                // space between messages
                message.Append(" ");

                message.AppendFormat(CultureInfo.CurrentCulture,
                    Strings.PackagesConfigConstraint,
                    problemPackageId,
                    configEntry.AllowedVersions.PrettyPrint(),
                    "packages.config");
            }

            return message.ToString();
        }

        /// <summary>
        /// Ex: PackageA (> 1.0.0)
        /// </summary>
        private static string FormatDependencyConstraint(ResolverPackage package, string dependencyId)
        {
            // The range may not exist, or may inclue all versions. For this reason we trim the string afterwards to remove extra spaces due to empty ranges
            var range = package.FindDependencyRange(dependencyId);
            var dependencyString = String.Format(CultureInfo.InvariantCulture, "{0} {1}", dependencyId,
                range == null ? string.Empty : range.PrettyPrint()).Trim();

            // A 1.0.0 dependency: B (= 1.5)
            return $"'{package.Id} {package.Version.ToString()} {Strings.DependencyConstraint}: {dependencyString}'";
        }

        private static IEnumerable<PackageDependency> GetBrokenDependencies(ResolverPackage package, IEnumerable<ResolverPackage> packages)
        {
            foreach (var dependency in package.Dependencies)
            {
                var target = packages.FirstOrDefault(targetPackage => StringComparer.OrdinalIgnoreCase.Equals(targetPackage.Id, dependency.Id));

                if (!IsDependencySatisfied(dependency, target))
                {
                    yield return dependency;
                }
            }

            yield break;
        }

        private static bool IsDependencySatisfied(PackageDependency dependency, ResolverPackage package)
        {
            return package != null && !package.Absent
                && (dependency.VersionRange == null || dependency.VersionRange.Satisfies(package.Version));
        }

        private static IEnumerable<ResolverPackage> GetPackagesWithBrokenDependenciesOnId(string targetId, IEnumerable<ResolverPackage> packages)
        {
            var targetPackage = packages.FirstOrDefault(package => StringComparer.OrdinalIgnoreCase.Equals(package.Id, targetId));

            foreach (var package in packages)
            {
                var range = package.FindDependencyRange(targetId);

                if (range != null && (targetPackage == null
                    || targetPackage.Version == null || !range.Satisfies(targetPackage.Version)))
                {
                    yield return package;
                }
            }

            yield break;
        }

        /// <summary>
        /// Find distance of a dependency from a target package.
        /// A -> B -> C
        /// C is 2 away from A
        /// </summary>
        /// <param name="packageId">package id</param>
        /// <param name="targets">required targets</param>
        /// <param name="packages">packages in the solution, only 1 package per id should exist</param>
        /// <returns>number of levels from a target</returns>
        public static int GetLowestDistanceFromTarget(string packageId, HashSet<string> targets, IEnumerable<ResolverPackage> packages)
        {
            // start with the target packages
            var walkedPackages = new HashSet<ResolverPackage>(packages.Where(package => targets.Contains(package.Id)), PackageIdentity.Comparer);

            int level = 0;

            // walk the packages, starting with the required packages until the given packageId is found
            // this is done in the simplest possible way to avoid circular dependencies
            // after 20 levels give up, the level is no longer important for ordering
            while (level < 20 && !walkedPackages.Any(package => StringComparer.OrdinalIgnoreCase.Equals(package.Id, packageId)))
            {
                level++;

                // find the next level of dependencies
                var dependencyIds = walkedPackages.SelectMany(package => package.Dependencies.Select(dependency => dependency.Id)).ToList();

                var dependencyPackages = packages.Where(package => dependencyIds.Contains(package.Id, StringComparer.OrdinalIgnoreCase));

                // add the dependency packages
                walkedPackages.UnionWith(dependencyPackages);
            }

            return level;
        }

        /// <summary>
        /// Sort packages in order of dependencies
        /// </summary>
        public static IEnumerable<ResolverPackage> TopologicalSort(IEnumerable<ResolverPackage> nodes)
        {
            var result = new List<ResolverPackage>();

            var dependsOn = new Func<ResolverPackage, ResolverPackage, bool>((x, y) =>
            {
                return x.FindDependencyRange(y.Id) != null;
            });

            var dependenciesAreSatisfied = new Func<ResolverPackage, bool>(node =>
            {
                var dependencies = node.Dependencies;
                return dependencies == null || !dependencies.Any() ||
                       dependencies.All(d => result.Any(r => StringComparer.OrdinalIgnoreCase.Equals(r.Id, d.Id)));
            });

            var satisfiedNodes = new HashSet<ResolverPackage>(nodes.Where(n => dependenciesAreSatisfied(n)));
            while (satisfiedNodes.Any())
            {
                // Pick any element from the set. Remove it, and add it to the result list.
                var node = satisfiedNodes.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).First();
                satisfiedNodes.Remove(node);
                result.Add(node);

                // Find unprocessed nodes that depended on the node we just added to the result.
                // If all of its dependencies are now satisfied, add it to the set of nodes to process.
                var newlySatisfiedNodes = nodes.Except(result)
                                               .Where(n => dependsOn(n, node))
                                               .Where(n => dependenciesAreSatisfied(n));

                foreach (var cur in newlySatisfiedNodes)
                {
                    satisfiedNodes.Add(cur);
                }
            }

            return result;
        }

        /// <summary>
        /// Check if two packages can exist in the same solution.
        /// This is used by the resolver.
        /// </summary>
        internal static bool ShouldRejectPackagePair(ResolverPackage p1, ResolverPackage p2)
        {
            var p1ToP2Dependency = p1.FindDependencyRange(p2.Id);
            if (p1ToP2Dependency != null)
            {
                return p2.Absent || !p1ToP2Dependency.Satisfies(p2.Version);
            }

            var p2ToP1Dependency = p2.FindDependencyRange(p1.Id);
            if (p2ToP1Dependency != null)
            {
                return p1.Absent || !p2ToP1Dependency.Satisfies(p1.Version);
            }

            return false;
        }

        /// <summary>
        /// Returns the first circular dependency found
        /// </summary>
        public static IEnumerable<ResolverPackage> FindCircularDependency(IEnumerable<ResolverPackage> solution)
        {
            // check each package to see if it is part of a loop, sort by id to keep the result deterministic
            foreach (var package in solution.OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase))
            {
                var result = FindCircularDependency(package, Enumerable.Empty<string>(), solution);

                if (result.Any())
                {
                    // loop found
                    return result;
                }
            }

            // no loops detected
            return Enumerable.Empty<ResolverPackage>();
        }

        private static IEnumerable<ResolverPackage> FindCircularDependency(ResolverPackage package, IEnumerable<string> parents, IEnumerable<ResolverPackage> solution)
        {
            // avoid checking depths beyond 20 packages deep
            if (parents.Count() < 20 && package != null && !package.Absent && package.Dependencies.Any())
            {
                // walk the dependencies
                foreach (var dependency in package.Dependencies.OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase))
                {
                    var dependencyPackage = solution.FirstOrDefault(solutionPackage => StringComparer.OrdinalIgnoreCase.Equals(solutionPackage.Id, dependency.Id));

                    if (parents.Contains(dependency.Id, StringComparer.OrdinalIgnoreCase))
                    {
                        // loop detected
                        return new ResolverPackage[] { package, dependencyPackage };
                    }

                    // recurse on dependencies
                    var result = FindCircularDependency(dependencyPackage, parents.Concat(new string[] { package.Id }), solution);

                    if (result.Any())
                    {
                        return (new ResolverPackage[] { package }).Concat(result);
                    }
                }
            }

            // end of the walk
            return Enumerable.Empty<ResolverPackage>();
        }
    }
}
