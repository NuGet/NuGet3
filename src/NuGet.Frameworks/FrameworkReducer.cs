﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace NuGet.Frameworks
{
    /// <summary>
    /// Reduces a list of frameworks into the smallest set of frameworks required.
    /// </summary>
    public class FrameworkReducer
    {
        private readonly IFrameworkNameProvider _mappings;
        private readonly IFrameworkCompatibilityProvider _compat;
        private readonly NuGetFrameworkFullComparer _fullComparer;
        private readonly NuGetFrameworkNameComparer _fwNameComparer;

        /// <summary>
        /// Creates a FrameworkReducer using the default framework mappings.
        /// </summary>
        public FrameworkReducer()
            : this(DefaultFrameworkNameProvider.Instance, DefaultCompatibilityProvider.Instance)
        {
        }

        /// <summary>
        /// Creates a FrameworkReducer using custom framework mappings.
        /// </summary>
        public FrameworkReducer(IFrameworkNameProvider mappings, IFrameworkCompatibilityProvider compat)
        {
            _mappings = mappings;
            _compat = compat;
            _fullComparer = new NuGetFrameworkFullComparer();
            _fwNameComparer = new NuGetFrameworkNameComparer();
        }

        /// <summary>
        /// Returns the nearest matching framework that is compatible.
        /// </summary>
        /// <param name="framework">Project target framework</param>
        /// <param name="possibleFrameworks">Possible frameworks to narrow down</param>
        /// <returns>Nearest compatible framework. If no frameworks are compatible null is returned.</returns>
        public NuGetFramework GetNearest(NuGetFramework framework, IEnumerable<NuGetFramework> possibleFrameworks)
        {
            var nearest = GetNearestInternal(framework, possibleFrameworks);

            var fallbackFramework = framework as FallbackFramework;

            if (fallbackFramework != null)
            {
                if (nearest == null || nearest.IsAny)
                {
                    nearest = GetNearestInternal(fallbackFramework.Fallback, possibleFrameworks);
                }
            }

            return nearest;
        }

        private NuGetFramework GetNearestInternal(NuGetFramework framework, IEnumerable<NuGetFramework> possibleFrameworks)
        {
            NuGetFramework nearest = null;

            // Unsupported frameworks always lose, throw them out unless it's all we were given
            if (possibleFrameworks.Any(e => e != NuGetFramework.UnsupportedFramework))
            {
                possibleFrameworks = possibleFrameworks.Where(e => e != NuGetFramework.UnsupportedFramework);
            }

            // Try exact matches first
            nearest = possibleFrameworks.Where(f => _fullComparer.Equals(framework, f)).FirstOrDefault();

            if (nearest == null)
            {
                // Elimate non-compatible frameworks
                var compatible = possibleFrameworks.Where(f => _compat.IsCompatible(framework, f));

                // Remove lower versions of compatible frameworks
                var reduced = ReduceUpwards(compatible);

                // Reduce to the same framework name if possible
                if (reduced.Count() > 1
                    && reduced.Any(f => _fwNameComparer.Equals(f, framework)))
                {
                    reduced = reduced.Where(f => _fwNameComparer.Equals(f, framework));
                }

                // PCL reduce
                if (reduced.Count() > 1)
                {
                    // if we have a pcl and non-pcl mix, throw out the pcls
                    if (reduced.Any(f => f.IsPCL)
                        && reduced.Any(f => !f.IsPCL))
                    {
                        reduced = reduced.Where(f => !f.IsPCL);
                    }
                    else if (reduced.All(f => f.IsPCL))
                    {
                        // decide between PCLs
                        if (framework.IsPCL)
                        {
                            reduced = GetNearestPCLtoPCL(framework, reduced);
                        }
                        else
                        {
                            reduced = GetNearestNonPCLtoPCL(framework, reduced);
                        }

                        if (reduced.Count() > 1)
                        {
                            // For scenarios where we are unable to decide between PCLs, choose the PCL with the 
                            // least frameworks. Less frameworks means less compatibility which means it is nearer to the target.
                            reduced = new NuGetFramework[] { GetBestPCL(reduced) };
                        }
                    }
                }

                // Profile reduce
                if (reduced.Count() > 1
                    && !reduced.Any(f => f.IsPCL))
                {
                    // Prefer the same framework and profile
                    if (framework.HasProfile)
                    {
                        var sameProfile = reduced.Where(f => _fwNameComparer.Equals(framework, f)
                                                             && StringComparer.OrdinalIgnoreCase.Equals(framework.Profile, f.Profile));

                        if (sameProfile.Any())
                        {
                            reduced = sameProfile;
                        }
                    }

                    // Prefer frameworks without profiles
                    if (reduced.Count() > 1
                        && reduced.Any(f => f.HasProfile)
                        && reduced.Any(f => !f.HasProfile))
                    {
                        reduced = reduced.Where(f => !f.HasProfile);
                    }
                }

                // if we have reduced down to a single framework, use that
                if (reduced.Count() == 1)
                {
                    nearest = reduced.Single();
                }

                // this should be a very rare occurrence
                // at this point we are unable to decide between the remaining frameworks in any useful way
                // just take the first one by rev alphabetical order if we can't narrow it down at all
                if (nearest == null && reduced.Any())
                {
                    // Sort by precedence rules, then by name in the case of a tie
                    nearest = reduced.OrderBy(f => f, new FrameworkPrecedenceSorter(_mappings))
                        .ThenByDescending(f => f, new NuGetFrameworkSorter())
                        .ThenBy(f => f.GetHashCode())
                        .First();
                }
            }

            return nearest;
        }

        /// <summary>
        /// Remove duplicates found in the equivalence mappings.
        /// </summary>
        public IEnumerable<NuGetFramework> Reduce(IEnumerable<NuGetFramework> frameworks)
        {
            // order first so we get consistent results for equivalent frameworks
            var input = frameworks.OrderBy(f => f.DotNetFrameworkName, StringComparer.OrdinalIgnoreCase).ToArray();

            var comparer = new NuGetFrameworkFullComparer();

            for (var i = 0; i < input.Length; i++)
            {
                var dupe = false;

                IEnumerable<NuGetFramework> eqFrameworks = null;
                if (!_mappings.TryGetEquivalentFrameworks(input[i], out eqFrameworks))
                {
                    eqFrameworks = new List<NuGetFramework>() { input[i] };
                }

                for (var j = i + 1; !dupe && j < input.Length; j++)
                {
                    dupe = eqFrameworks.Contains(input[j], comparer);
                }

                if (!dupe)
                {
                    yield return input[i];
                }
            }

            yield break;
        }

        /// <summary>
        /// Reduce to the highest framework
        /// Ex: net45, net403, net40 -> net45
        /// </summary>
        public IEnumerable<NuGetFramework> ReduceUpwards(IEnumerable<NuGetFramework> frameworks)
        {
            // NuGetFramework.AnyFramework is a special case
            if (frameworks.Any(e => e != NuGetFramework.AnyFramework))
            {
                // Remove all instances of Any unless it is the only one in the list
                frameworks = frameworks.Where(e => e != NuGetFramework.AnyFramework);
            }

            // x: net40 j: net45 -> remove net40
            // x: wp8 j: win8 -> keep wp8
            return ReduceCore(frameworks, (x, y) => _compat.IsCompatible(y, x)).ToArray();
        }

        /// <summary>
        /// Reduce to the lowest framework
        /// Ex: net45, net403, net40 -> net40
        /// </summary>
        public IEnumerable<NuGetFramework> ReduceDownwards(IEnumerable<NuGetFramework> frameworks)
        {
            // NuGetFramework.AnyFramework is a special case
            if (frameworks.Any(e => e == NuGetFramework.AnyFramework))
            {
                // Any is always the lowest
                return new NuGetFramework[] { NuGetFramework.AnyFramework };
            }

            return ReduceCore(frameworks, (x, y) => _compat.IsCompatible(x, y)).ToArray();
        }

        private IEnumerable<NuGetFramework> ReduceCore(IEnumerable<NuGetFramework> frameworks, Func<NuGetFramework, NuGetFramework, bool> isCompat)
        {
            // remove duplicate frameworks
            var input = frameworks.Distinct(_fullComparer).ToArray();

            var results = new List<NuGetFramework>(input.Length);

            for (var i = 0; i < input.Length; i++)
            {
                var dupe = false;

                var x = input[i];

                for (var j = 0; !dupe && j < input.Length; j++)
                {
                    if (j != i)
                    {
                        var y = input[j];

                        // remove frameworks that are compatible with other frameworks in the list
                        // do not remove frameworks which tie with others, for example: net40 and net40-client
                        // these equivalent frameworks should both be returned to let the caller decide between them
                        if (isCompat(x, y))
                        {
                            var revCompat = isCompat(y, x);

                            dupe = !revCompat;

                            // for scenarios where the framework identifiers are the same dupe the zero version
                            // Ex: win, win8 - these are equivalent, but only one is needed
                            if (revCompat && _fwNameComparer.Equals(x, y))
                            {
                                // Throw out the zero version
                                // Profile, Platform, and all other aspects should have been covered by the compat check already
                                dupe = x.Version == FrameworkConstants.EmptyVersion && y.Version != FrameworkConstants.EmptyVersion;
                            }
                        }
                    }
                }

                if (!dupe)
                {
                    results.Add(input[i]);
                }
            }

            // sort the results just to make this more deterministic for the callers
            return results.OrderBy(f => f.Framework, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.ToString());
        }

        private IEnumerable<NuGetFramework> GetNearestNonPCLtoPCL(NuGetFramework framework, IEnumerable<NuGetFramework> reduced)
        {
            // If framework is not a PCL, find the PCL with the sub framework nearest to framework
            // Collect all frameworks from all PCLs we are considering
            var pclToFrameworks = ExplodePortableFrameworks(reduced);
            var allPclFrameworks = pclToFrameworks.Values.SelectMany(f => f);

            // Find the nearest (no PCLs are involved at this point)
            Debug.Assert(allPclFrameworks.All(f => !f.IsPCL), "a PCL returned a PCL as its profile framework");
            var nearestProfileFramework = GetNearest(framework, allPclFrameworks);

            // Reduce to only PCLs that include the nearest match
            reduced = pclToFrameworks.Where(pair =>
                pair.Value.Contains(nearestProfileFramework, NuGetFramework.Comparer))
                .Select(pair => pair.Key);

            return reduced;
        }

        private IEnumerable<NuGetFramework> GetNearestPCLtoPCL(NuGetFramework framework, IEnumerable<NuGetFramework> reduced)
        {
            // Compare each framework in the target framework individually
            // against the list of possible PCLs. This effectively lets
            // each sub-framework vote on which PCL is nearest.
            var subFrameworks = ExplodePortableFramework(framework);

            // reduce the sub frameworks - this would only have an effect if the PCL is 
            // poorly formed and contains duplicates such as portable-win8+win81
            subFrameworks = Reduce(subFrameworks);

            // Find all frameworks in all PCLs
            var pclToFrameworks = ExplodePortableFrameworks(reduced);
            var allPclFrameworks = pclToFrameworks.Values.SelectMany(f => f).Distinct(_fullComparer);

            var scores = new Dictionary<NuGetFramework, int>(NuGetFramework.Comparer);

            // find the nearest PCL for each framework
            foreach (var sub in subFrameworks)
            {
                Debug.Assert(!sub.IsPCL, "a PCL returned a PCL as its profile framework");

                // from all possible frameworks, find the best match
                var nearestForSub = GetNearest(sub, allPclFrameworks);

                // +1 each framework containing the best match
                foreach (var pair in pclToFrameworks)
                {
                    if (pair.Value.Contains(nearestForSub, _fullComparer))
                    {
                        if (!scores.ContainsKey(pair.Key))
                        {
                            scores.Add(pair.Key, 1);
                        }
                        else
                        {
                            scores[pair.Key]++;
                        }
                    }
                }
            }

            // take the highest vote count, this will be at least one
            reduced = scores.GroupBy(pair => pair.Value).OrderByDescending(g => g.Key).First().Select(e => e.Key);

            return reduced;
        }

        /// <summary>
        /// Create lookup of the given PCLs to their actual frameworks
        /// </summary>
        private Dictionary<NuGetFramework, IEnumerable<NuGetFramework>> ExplodePortableFrameworks(IEnumerable<NuGetFramework> pcls, bool includeOptional = true)
        {
            var result = new Dictionary<NuGetFramework, IEnumerable<NuGetFramework>>(NuGetFramework.Comparer);

            foreach (var pcl in pcls)
            {
                var frameworks = ExplodePortableFramework(pcl);
                result.Add(pcl, frameworks);
            }

            return result;
        }

        /// <summary>
        /// portable-net45+win8 -> net45, win8
        /// </summary>
        private IEnumerable<NuGetFramework> ExplodePortableFramework(NuGetFramework pcl, bool includeOptional = true)
        {
            IEnumerable<NuGetFramework> frameworks = null;
            if (!_mappings.TryGetPortableFrameworks(pcl.Profile, includeOptional, out frameworks))
            {
                Debug.Fail("Unable to get portable frameworks from: " + pcl.ToString());
                frameworks = Enumerable.Empty<NuGetFramework>();
            }

            return frameworks;
        }

        /// <summary>
        /// Order PCLs when there is no other way to decide.
        /// </summary>
        private NuGetFramework GetBestPCL(IEnumerable<NuGetFramework> reduced)
        {
            NuGetFramework current = null;

            foreach (var considering in reduced)
            {
                if (current == null
                    || IsBetterPCL(current, considering))
                {
                    current = considering;
                }
            }

            return current;
        }

        /// <summary>
        /// Sort PCLs using these criteria
        /// 1. Lowest number of frameworks (highest surface area) wins first
        /// 2. Profile with the highest version numbers wins next
        /// 3. String compare is used as a last resort
        /// </summary>
        private bool IsBetterPCL(NuGetFramework current, NuGetFramework considering)
        {
            Debug.Assert(considering.IsPCL && current.IsPCL, "This method should be used only to compare PCLs");

            // Find all frameworks in the profile
            var consideringFrameworks = ExplodePortableFramework(considering, false);

            var currentFrameworks = ExplodePortableFramework(current, false);

            // The PCL with the least frameworks (highest surface area) goes first
            if (consideringFrameworks.Count() < currentFrameworks.Count())
            {
                return true;
            }
            else if (currentFrameworks.Count() < consideringFrameworks.Count())
            {
                return false;
            }

            // If both frameworks have the same number of frameworks take the framework that has the highest 
            // overall set of framework versions

            // Find Frameworks that both profiles have in common
            var sharedFrameworkIds = consideringFrameworks.Select(f => f.Framework)
                .Where(f =>
                    currentFrameworks.Any(consideringFramework => StringComparer.OrdinalIgnoreCase.Equals(f, consideringFramework.Framework)));

            var consideringHighest = 0;
            var currentHighest = 0;

            // Determine which framework has the highest version of each shared framework
            foreach (var sharedId in sharedFrameworkIds)
            {
                var consideringFramework = consideringFrameworks.Where(f => StringComparer.OrdinalIgnoreCase.Equals(f.Framework, sharedId)).First();
                var currentFramework = currentFrameworks.Where(f => StringComparer.OrdinalIgnoreCase.Equals(f.Framework, sharedId)).First();

                if (consideringFramework.Version < currentFramework.Version)
                {
                    currentHighest++;
                }
                else if (currentFramework.Version < consideringFramework.Version)
                {
                    consideringHighest++;
                }
            }

            // Prefer the highest count
            if (currentHighest < consideringHighest)
            {
                return true;
            }
            else if (consideringHighest < currentHighest)
            {
                return false;
            }

            // Take the highest version of .NET if no winner could be determined, this is usually a good indicator of which is newer
            var consideringNet = consideringFrameworks.Where(f => StringComparer.OrdinalIgnoreCase.Equals(f.Framework, FrameworkConstants.FrameworkIdentifiers.Net)).FirstOrDefault();
            var currentNet = currentFrameworks.Where(f => StringComparer.OrdinalIgnoreCase.Equals(f.Framework, FrameworkConstants.FrameworkIdentifiers.Net)).FirstOrDefault();

            // Compare using .NET only if both frameworks have it. PCLs should always have .NET, but since users can make these strings up we should
            // try to handle that as best as possible.
            if (consideringNet != null
                && currentNet != null)
            {
                if (currentNet.Version < consideringNet.Version)
                {
                    return true;
                }
                else if (consideringNet.Version < currentNet.Version)
                {
                    return false;
                }
            }

            // In the very rare case that both frameworks are still equal, we have to pick one. 
            // There is nothing but we need to be deterministic, so compare the profiles as strings.
            if (StringComparer.OrdinalIgnoreCase.Compare(considering.GetShortFolderName(_mappings), current.GetShortFolderName(_mappings)) < 0)
            {
                return true;
            }

            return false;
        }
    }
}
