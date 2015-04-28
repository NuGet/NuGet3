﻿using NuGet.LibraryModel;
using NuGet.ProjectModel;
using System.Collections.Generic;
using System.Linq;

namespace NuGet.Strawman.Commands
{
    public class RestoreResult
    {
        public bool Success { get; }
        
        /// <summary>
        /// Gets the resolved dependency graphs produced by the restore operation
        /// </summary>
        public IEnumerable<RestoreTargetGraph> RestoreGraphs { get; }

        /// <summary>
        /// Gets the lock file that was generated during the restore or, in the case of a locked lock file,
        /// was used to determine the packages to install during the restore.
        /// </summary>
        /// <remarks>
        /// May be null if the restore did not complete successfully
        /// </remarks>
        public LockFile LockFile { get; }

        public RestoreResult(bool success, IEnumerable<RestoreTargetGraph> restoreGraphs)
            : this(success, restoreGraphs, lockfile: null)
        { }

        public RestoreResult(bool success, IEnumerable<RestoreTargetGraph> restoreGraphs, LockFile lockfile)
        {
            Success = success;
            RestoreGraphs = restoreGraphs;
            LockFile = lockfile;
        }

        /// <summary>
        /// Calculates the complete set of all packages installed by this operation
        /// </summary>
        /// <remarks>
        /// This requires quite a bit of iterating over the graph so the result should be cached
        /// </remarks>
        /// <returns>A set of libraries that were installed by this operation</returns>
        public ISet<LibraryIdentity> GetAllInstalled()
        {
            return new HashSet<LibraryIdentity>(RestoreGraphs.Where(g => !g.InConflict).SelectMany(g => g.Install).Distinct().Select(m => m.Library));
        }

        /// <summary>
        /// Calculates the complete set of all unresolved dependencies for this operation
        /// </summary>
        /// <remarks>
        /// This requires quite a bit of iterating over the graph so the result should be cached
        /// </remarks>
        /// <returns>A set of dependencies that were unable to be resolved by this operation</returns>
        public ISet<LibraryRange> GetAllUnresolved()
        {
            return new HashSet<LibraryRange>(RestoreGraphs.SelectMany(g => g.Unresolved).Distinct());
        }
    }
}