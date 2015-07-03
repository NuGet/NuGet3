﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using NuGet.Logging;

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

        private static bool TrimGraph<TItem>(this GraphNode<TItem> root)
        {
            // A -> B -> A

            // A -> B -> C -> D 2.0
            //        -> D 1.0
            // Check for cycles and nearest win situations where there's a potential downgrade

            var nodes = new List<Tuple<GraphNode<TItem>, GraphNode<TItem>>>();

            root.ForEach(node =>
            {
                if (node.Disposition != Disposition.PotentiallyDowngraded)
                {
                    return;
                }

                bool downgraded = false;

                // TODO: This could be more efficient
                for (var n = node.OuterNode; !downgraded && n != null; n = n.OuterNode)
                {
                    foreach (var sideNode in n.InnerNodes)
                    {
                        if (sideNode != node &&
                            sideNode.Key != node.Key &&
                            sideNode.Key.Name == node.Key.Name)
                        {
                            nodes.Add(Tuple.Create(node, sideNode));

                            downgraded = true;

                            break;
                        }
                    }
                }

                if (node.Disposition == Disposition.PotentiallyDowngraded)
                {
                    nodes.Add(Tuple.Create(node, (GraphNode<TItem>)null));
                }
            });

            var sb = new StringBuilder();

            // Remove the bad nodes
            nodes.ForEach(n =>
            {
                var downgraded = n.Item1;
                var downgradedBy = n.Item2;

                // We need to remove the node from the tree since it's not actually resolved and
                // other layers down the stack will check it
                downgraded.OuterNode.InnerNodes.Remove(downgraded);

                if (downgradedBy != null)
                {
                    sb.AppendLine($"Attempting to downgrade {downgraded.Key.Name} from {downgraded.Key.VersionRange.MinVersion} to {downgradedBy.Key.VersionRange.MinVersion}");
                    sb.AppendLine(GetPath(downgraded));
                    sb.AppendLine(GetPath(downgradedBy));
                    sb.AppendLine();
                }
            });

            // An exception is pretty terrrible
            if (sb.Length > 0)
            {
                throw new InvalidOperationException(sb.ToString());
            }

            return true;
        }

        private static string GetPath<TItem>(GraphNode<TItem> node)
        {
            var result = "";
            var current = node;

            while (current != null)
            {
                result = string.IsNullOrEmpty(result) ? current.Key.ToString() : current.Key + " -> " + result;
                current = current.OuterNode;
            }

            return result;
        }

        public static bool TryResolveConflicts<TItem>(this GraphNode<TItem> root)
        {
            if (!root.TrimGraph())
            {
                return false;
            }

            // now we walk the tree as often as it takes to determine 
            // which paths are accepted or rejected, based on conflicts occuring
            // between cousin packages

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
                            node.Disposition = tracker.IsBestVersion(node.Item) ? Disposition.Accepted : Disposition.Rejected;
                        }

                        return node.Disposition == Disposition.Accepted;
                    });

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
