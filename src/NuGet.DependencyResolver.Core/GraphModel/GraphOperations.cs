﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using NuGet.LibraryModel;
using NuGet.Logging;
using NuGet.DependencyResolver.Core;

namespace NuGet.DependencyResolver
{
    public static class GraphOperations
    {
        private enum WalkState
        {
            Walking,
            Rejected,
            Ambiguous
        }

        public static void CheckCycleAndNearestWins<TItem>(this GraphNode<TItem> root,
                                                           List<Tuple<GraphNode<TItem>, GraphNode<TItem>>> downgrades, 
                                                           List<GraphNode<TItem>> cycles)
        {
            // Cycle
            
            // A -> B -> A (cycle)

            // Downgrade
            
            // A -> B -> C -> D 2.0 (downgrage)
            //        -> D 1.0

            // Potential downgrade that turns out to not downgrade
            // This should never happen in practice since B would have never been valid to begin with.

            // A -> B -> C -> D 2.0
            //        -> D 1.0
            //   -> D 2.0

            root.ForEach(node =>
            {
                if (node.Disposition == Disposition.Cycle)
                {
                    cycles.Add(node);

                    // Remove this node from the tree so the nothing else evaluates this.
                    // This is ok since we have a parent pointer and we can still print the path
                    node.OuterNode.InnerNodes.Remove(node);

                    return;
                }

                if (node.Disposition != Disposition.PotentiallyDowngraded)
                {
                    return;
                }

                bool downgraded = false;

                // REVIEW: This could probably be done in a single pass where we keep track
                // of what is nearer as we walk down the graph (BFS)
                for (var n = node.OuterNode; !downgraded && n != null; n = n.OuterNode)
                {
                    foreach (var sideNode in n.InnerNodes)
                    {
                        if (sideNode != node &&
                            sideNode.Key != node.Key &&
                            sideNode.Key.Name == node.Key.Name)
                        {
                            downgrades.Add(Tuple.Create(node, sideNode));

                            downgraded = true;

                            break;
                        }
                    }
                }

                if (node.Disposition == Disposition.PotentiallyDowngraded)
                {
                    // Remove this node from the tree so the nothing else evaluates this.
                    // This is ok since we have a parent pointer and we can still print the path
                    node.OuterNode.InnerNodes.Remove(node);
                }
            });
        }

        public static string GetPath<TItem>(this GraphNode<TItem> node)
        {
            var result = "";
            var current = node;

            while (current != null)
            {
                result = (current.Item?.Key ?? current.Key).ToString() + (string.IsNullOrEmpty(result) ? "" : " -> " + result);
                current = current.OuterNode;
            }

            return result;
        }

        public static bool TryResolveConflicts<TItem>(this GraphNode<TItem> root)
        {
            // now we walk the tree as often as it takes to determine 
            // which paths are accepted or rejected, based on conflicts occuring
            // between cousin packages

            var acceptedLibraries = new Dictionary<string, LibraryIdentity>(StringComparer.OrdinalIgnoreCase);

            var patience = 1000;
            var incomplete = true;
            while (incomplete && --patience != 0)
            {
                // Create a picture of what has not been rejected yet
                var tracker = new Tracker<TItem>();

                root.ForEach(true, (node, state) =>
                    {
                        if (!state
                            || node.Disposition == Disposition.Rejected)
                        {
                            // Mark all nodes as rejected if they aren't already marked
                            node.Disposition = Disposition.Rejected;
                            return false;
                        }

                        // HACK(anurse): Reference nodes win all battles.
                        if (node.Item.Key.Type == "Reference")
                        {
                            tracker.Lock(node.Item);
                        }
                        else
                        {
                            tracker.Track(node.Item);
                        }
                        return true;
                    });

                // Inform tracker of ambiguity beneath nodes that are not resolved yet
                // between:
                // a1->b1->d1->x1
                // a1->c1->d2->z1
                // first attempt
                //  d1/d2 are considered disputed 
                //  x1 and z1 are considered ambiguous
                //  d1 is rejected
                // second attempt
                //  d1 is rejected, d2 is accepted
                //  x1 is no longer seen, and z1 is not ambiguous
                //  z1 is accepted
                root.ForEach(WalkState.Walking, (node, state) =>
                    {
                        if (node.Disposition == Disposition.Rejected)
                        {
                            return WalkState.Rejected;
                        }

                        if (state == WalkState.Walking
                            && tracker.IsDisputed(node.Item))
                        {
                            return WalkState.Ambiguous;
                        }

                        if (state == WalkState.Ambiguous)
                        {
                            tracker.MarkAmbiguous(node.Item);
                        }

                        return state;
                    });

                // Now mark unambiguous nodes as accepted or rejected
                root.ForEach(true, (node, state) =>
                    {
                        if (!state
                            || node.Disposition == Disposition.Rejected)
                        {
                            return false;
                        }

                        if (tracker.IsAmbiguous(node.Item))
                        {
                            return false;
                        }

                        if (node.Disposition == Disposition.Acceptable)
                        {
                            if (tracker.IsBestVersion(node.Item))
                            {
                                node.Disposition = Disposition.Accepted;
                                acceptedLibraries[node.Key.Name] = node.Item.Key;
                            }
                            else
                            {
                                node.Disposition = Disposition.Rejected;
                            }
                        }

                        return node.Disposition == Disposition.Accepted;
                    });

                // Locate nodes that were formerly disputed that do not have resolutions
                var unresolvableVersionSpecifications = new HashSet<string>(StringComparer.Ordinal);

                root.ForEach(true, (node, state) =>
                {
                    if (node.Disposition == Disposition.Rejected)
                    {
                        LibraryIdentity resolvedLibrary;
                        if (acceptedLibraries.TryGetValue(node.Key.Name, out resolvedLibrary) &&
                            !node.Key.VersionRange.Satisfies(resolvedLibrary.Version))
                        {
                            unresolvableVersionSpecifications.Add(
                                Strings.FormatError_FailedToResolveConflicts(node.Key.Name, node.Key.VersionRange));

                            return false;
                        }
                    }

                    return true;
                });

                if (unresolvableVersionSpecifications.Count > 0)
                {
                    throw new InvalidOperationException(string.Join(Environment.NewLine, unresolvableVersionSpecifications));
                }

                incomplete = false;

                root.ForEach(node => incomplete |= node.Disposition == Disposition.Acceptable);
            }

            return !incomplete;
        }

        public static void ForEach<TItem, TState>(this GraphNode<TItem> root, TState state, Func<GraphNode<TItem>, TState, TState> visitor)
        {
            // breadth-first walk of Node tree

            var queue = new Queue<Tuple<GraphNode<TItem>, TState>>();
            queue.Enqueue(Tuple.Create(root, state));
            while (queue.Count > 0)
            {
                var work = queue.Dequeue();
                var innerState = visitor(work.Item1, work.Item2);
                foreach (var innerNode in work.Item1.InnerNodes)
                {
                    queue.Enqueue(Tuple.Create(innerNode, innerState));
                }
            }
        }

        public static void ForEach<TItem>(this IEnumerable<GraphNode<TItem>> roots, Action<GraphNode<TItem>> visitor)
        {
            foreach (var root in roots)
            {
                root.ForEach(visitor);
            }
        }

        public static void ForEach<TItem>(this GraphNode<TItem> root, Action<GraphNode<TItem>> visitor)
        {
            // breadth-first walk of Node tree, without TState parameter
            ForEach(root, 0, (node, _) =>
                {
                    visitor(node);
                    return 0;
                });
        }

        // Box Drawing Unicode characters:
        // http://www.unicode.org/charts/PDF/U2500.pdf
        private const char LIGHT_HORIZONTAL = '\u2500';
        private const char LIGHT_UP_AND_RIGHT = '\u2514';
        private const char LIGHT_VERTICAL_AND_RIGHT = '\u251C';

        public static void Dump<TItem>(this GraphNode<TItem> root, Action<string> write)
        {
            DumpNode(root, write, level: 0);
            DumpChildren(root, write, level: 0);
        }

        private static void DumpChildren<TItem>(GraphNode<TItem> root, Action<string> write, int level)
        {
            var children = root.InnerNodes;
            for (var i = 0; i < children.Count; i++)
            {
                DumpNode(children[i], write, level + 1);
                DumpChildren(children[i], write, level + 1);
            }
        }

        private static void DumpNode<TItem>(GraphNode<TItem> node, Action<string> write, int level)
        {
            var output = new StringBuilder();
            if (level > 0)
            {
                output.Append(LIGHT_VERTICAL_AND_RIGHT);
                output.Append(new string(LIGHT_HORIZONTAL, level));
                output.Append(" ");
            }

            output.Append($"{node.Key} ({node.Disposition})");

            if (node.Item != null
                && node.Item.Key != null)
            {
                output.Append($" => {node.Item.Key.ToString()}");
            }
            else
            {
                output.Append($" => ???");
            }
            write(output.ToString());
        }
    }
}
