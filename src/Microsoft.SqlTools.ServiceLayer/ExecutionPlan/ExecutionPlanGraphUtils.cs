//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.SqlTools.ServiceLayer.ExecutionPlan.ShowPlan;
using Microsoft.SqlTools.Utility;
using System.Diagnostics;
using Microsoft.SqlTools.ServiceLayer.ExecutionPlan.Contracts;

namespace Microsoft.SqlTools.ServiceLayer.ExecutionPlan
{
    public class ExecutionPlanGraphUtils
    {
        public static List<ExecutionPlanGraph> CreateShowPlanGraph(string xml, string fileName)
        {
            ShowPlanGraph[] graphs = ShowPlanGraph.ParseShowPlanXML(xml, ShowPlan.ShowPlanType.Unknown);
            return graphs.Select((g, index) => new ExecutionPlanGraph
            {
                Root = ConvertShowPlanTreeToExecutionPlanTree(g.Root),
                Query = g.Statement,
                GraphFile = new ExecutionPlanGraphInfo
                {
                    GraphFileContent = xml,
                    GraphFileType = "xml",
                    PlanIndexInFile = index
                },
                Recommendations = ParseRecommendations(g, fileName)
            }).ToList();
        }

        public static ExecutionPlanNode ConvertShowPlanTreeToExecutionPlanTree(Node currentNode)
        {   
            return new ExecutionPlanNode
            {
                ID = currentNode.ID,
                Type = currentNode.Operation.Image,
                Cost = currentNode.Cost,
                SubTreeCost = currentNode.SubtreeCost,
                Description = currentNode.Description,
                Subtext = currentNode.GetDisplayLinesOfText(true),
                RelativeCost = currentNode.RelativeCost,
                Properties = GetProperties(currentNode.Properties),
                Children = currentNode.Children.Select(x => ConvertShowPlanTreeToExecutionPlanTree(x)).ToList(),
                Edges = currentNode.Edges.Select(x => ConvertShowPlanEdgeToExecutionPlanEdge(x)).ToList(),
                Badges = GenerateNodeOverlay(currentNode),
                Name = currentNode.DisplayName,
                ElapsedTimeInMs = currentNode.ElapsedTimeInMs
            };
        }

        public static List<Badge> GenerateNodeOverlay(Node currentNode)
        {
            List<Badge> overlays = new List<Badge>();

            if (currentNode.HasWarnings)
            {
                if (currentNode.HasCriticalWarnings)
                {
                    overlays.Add(new Badge
                    {
                        Type = BadgeType.CriticalWarning,
                        Tooltip = SR.WarningOverlayTooltip
                    });
                }
                else
                {
                    overlays.Add(new Badge
                    {
                        Type = BadgeType.Warning,
                        Tooltip = SR.WarningOverlayTooltip
                    });
                }
            }
            if (currentNode.IsParallel)
            {
                overlays.Add(new Badge
                {
                    Type = BadgeType.Parallelism,
                    Tooltip = SR.ParallelismOverlayTooltip
                });
            }
            return overlays;
        }

        public static ExecutionPlanEdges ConvertShowPlanEdgeToExecutionPlanEdge(Edge edge)
        {
            return new ExecutionPlanEdges
            {
                RowCount = edge.RowCount,
                RowSize = edge.RowSize,
                Properties = GetProperties(edge.Properties)
            };
        }

        public static List<ExecutionPlanGraphPropertyBase> GetProperties(PropertyDescriptorCollection props)
        {
            List<ExecutionPlanGraphPropertyBase> propsList = new List<ExecutionPlanGraphPropertyBase>();
            foreach (PropertyValue prop in props)
            {
                var complexProperty = prop.Value as ExpandableObjectWrapper;
                if (complexProperty == null)
                {
                    if(!prop.IsBrowsable)
                    {
                        continue;
                    }
                    var propertyValue = prop.DisplayValue;
                    var propertyDataType = PropertyValueDataType.String;
                    switch (prop.Value)
                    {
                        case string stringValue:
                            propertyDataType = PropertyValueDataType.String;
                            break;
                        case int integerValue:
                        case long longIntegerValue:
                        case uint unsignedIntegerValue:
                        case ulong unsignedLongValue:
                        case float floatValue:
                        case double doubleValue:
                            propertyDataType = PropertyValueDataType.Number;
                            break;
                        case bool booleanValue:
                            propertyDataType = PropertyValueDataType.Boolean;
                            break;
                        default:
                            propertyDataType = PropertyValueDataType.String;
                            break;
                    }
                    propsList.Add(new ExecutionPlanGraphProperty()
                    {
                        Name = prop.DisplayName,
                        Value = propertyValue,
                        ShowInTooltip = prop.ShowInTooltip,
                        DisplayOrder = prop.DisplayOrder,
                        PositionAtBottom = prop.IsLongString,
                        DisplayValue = GetPropertyDisplayValue(prop),
                        BetterValue = prop.BetterValue,
                        DataType = propertyDataType
                    });
                }
                else
                {
                    var propertyValue = GetProperties(complexProperty.Properties);
                    propsList.Add(new NestedExecutionPlanGraphProperty()
                    {
                        Name = prop.DisplayName,
                        Value = propertyValue,
                        ShowInTooltip = prop.ShowInTooltip,
                        DisplayOrder = prop.DisplayOrder,
                        PositionAtBottom = prop.IsLongString,
                        DisplayValue = GetPropertyDisplayValue(prop),
                        BetterValue = prop.BetterValue,
                        DataType = PropertyValueDataType.Nested
                    });
                }

            }
            return propsList;
        }

        private static List<ExecutionPlanRecommendation> ParseRecommendations(ShowPlanGraph g, string fileName)
        {
            return g.Description.MissingIndices.Select(mi => new ExecutionPlanRecommendation
            {
                DisplayString = mi.MissingIndexCaption,
                Query = mi.MissingIndexQueryText,
                QueryWithDescription = ParseMissingIndexQueryText(fileName, mi.MissingIndexImpact, mi.MissingIndexDatabase, mi.MissingIndexQueryText)
            }).ToList();
        }

        /// <summary>
        /// Creates query file text for the recommendations. It has the missing index query along with some lines of description.
        /// </summary>
        /// <param name="fileName">query file name that has generated the plan</param>
        /// <param name="impact">impact of the missing query on performance</param>
        /// <param name="database">database name to create the missing index in</param>
        /// <param name="query">actual query that will be used to create the missing index</param>
        /// <returns></returns>
        private static string ParseMissingIndexQueryText(string fileName, string impact, string database, string query)
        {
            return $@"{SR.MissingIndexDetailsTitle(fileName, impact)}

/*
{string.Format("USE {0}", database)}
GO
{string.Format("{0}", query)}
GO
*/
";
        }

        private static string GetPropertyDisplayValue(PropertyValue property)
        {
            try
            {
                // Get the property value.
                object propertyValue = property.GetValue(property.Value);

                if (propertyValue == null)
                {
                    return String.Empty;
                }

                // Convert the property value to the text.
                return property.Converter.ConvertToString(propertyValue).Trim();
            }
            catch (Exception e)
            {
                Logger.Write(TraceEventType.Error, e.ToString());
                return String.Empty;
            }
        }

        public static void CopyMatchingNodesIntoSkeletonDTO(ExecutionGraphComparisonResult destRoot, ExecutionGraphComparisonResult srcRoot)
        {
            var srcGraphLookupTable = srcRoot.CreateSkeletonLookupTable();

            var queue = new Queue<ExecutionGraphComparisonResult>();
            queue.Enqueue(destRoot);

            while (queue.Count != 0)
            {
                var curNode = queue.Dequeue();

                for (int index = 0; index < curNode.MatchingNodesId.Count; ++index)
                {
                    var matchingId = curNode.MatchingNodesId[index];
                    curNode.MatchingNodesId[index] = matchingId;
                }

                foreach (var child in curNode.Children)
                {
                    queue.Enqueue(child);
                }
            }
        }
    }

    public static class ExecutionGraphComparisonResultExtensions
    {
        public static Dictionary<int, ExecutionGraphComparisonResult> CreateSkeletonLookupTable(this ExecutionGraphComparisonResult node)
        {
            var skeletonNodeTable = new Dictionary<int, ExecutionGraphComparisonResult>();
            var queue = new Queue<ExecutionGraphComparisonResult>();
            queue.Enqueue(node);

            while (queue.Count != 0)
            {
                var curNode = queue.Dequeue();

                if (!skeletonNodeTable.ContainsKey(curNode.BaseNode.ID))
                {
                    skeletonNodeTable[curNode.BaseNode.ID] = curNode;
                }

                foreach (var child in curNode.Children)
                {
                    queue.Enqueue(child);
                }
            }

            return skeletonNodeTable;
        }
    }
}
