using System;
using System.Collections.Generic;
using System.Text;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// Checks a <see cref="DialogueDefinition"/> for content mistakes before a
    /// player can trip over them. Plain C#: <c>OnValidate</c> runs it on every
    /// edit, and an EditMode test runs it over every definition in the project.
    ///
    /// <b>Errors</b> (the asset test fails): no conversation id on a graph; an
    /// empty or duplicate node id; an empty or unknown start id; a next, else,
    /// fallback or option link to a node that doesn't exist; a choice with no
    /// options, or more than <see cref="MaxOptions"/>; an empty or duplicate
    /// option id; a choice whose every option has conditions and that has no
    /// fallback, so it could strand the player with nothing to pick; branches
    /// that loop into each other with nothing shown in between; a reachable
    /// loop with no way out (cycles are fine as long as some path from them
    /// ends the talk; one that can't would hold the player in a stopped world,
    /// so the runner also ends the talk at its door).
    ///
    /// <b>Warnings</b>: a node unreachable from the start; a branch with no
    /// conditions; an empty line or option; an option too long for two
    /// rows; a condition or effect with no key; a "chose" condition naming a
    /// node or option this conversation doesn't have; an option leading
    /// straight to a choice with no prompt (its answers would appear
    /// under the old question); a graph that still carries linear lines
    /// (ignored); a linear definition with no lines.
    ///
    /// The analysis is static and optimistic: an exit behind a condition still
    /// counts as an exit.
    /// </summary>
    public static class DialogueValidator
    {
        /// <summary>Most options one choice may offer: the option list has this many
        /// rows, and the number keys go 1–9.</summary>
        public const int MaxOptions = 9;

        /// <summary>Option text longer than this probably won't fit the column's two
        /// lines (it wraps past about 30 characters).</summary>
        public const int MaxOptionLength = 60;

        /// <summary>All issues in <paramref name="definition"/>, errors and warnings, in
        /// the order found.</summary>
        public static List<DialogueIssue> Validate(DialogueDefinition definition)
        {
            var issues = new List<DialogueIssue>();
            Validate(definition, issues);
            return issues;
        }

        /// <summary>True if any issue is an error.</summary>
        public static bool HasErrors(IReadOnlyList<DialogueIssue> issues)
        {
            if (issues == null) return false;
            for (int i = 0; i < issues.Count; i++)
                if (issues[i].IsError) return true;
            return false;
        }

        /// <summary>One line per issue, errors first, with a count heading.</summary>
        public static string Describe(IReadOnlyList<DialogueIssue> issues)
        {
            if (issues == null || issues.Count == 0) return "no issues";
            int errors = 0;
            foreach (var issue in issues) if (issue.IsError) errors++;
            var text = new StringBuilder();
            text.Append(errors).Append(" error(s), ").Append(issues.Count - errors).Append(" warning(s)");
            foreach (var issue in issues) if (issue.IsError) text.Append("\n  ").Append(issue);
            foreach (var issue in issues) if (!issue.IsError) text.Append("\n  ").Append(issue);
            return text.ToString();
        }

        /// <summary>Clears <paramref name="issues"/> and fills it.</summary>
        public static void Validate(DialogueDefinition definition, List<DialogueIssue> issues)
        {
            issues.Clear();
            if (definition == null)
            {
                Error(issues, "", "No definition.");
                return;
            }
            if (!definition.HasGraph)
            {
                if (definition.LinearLines.Count == 0)
                    Warn(issues, "", "No nodes and no lines: nobody can talk with this.");
                return;
            }

            var nodes = definition.Nodes;
            string conversation = definition.Id;
            if (string.IsNullOrWhiteSpace(conversation))
                Error(issues, "", "The conversation id is empty. Choices are recorded under it; give it a " +
                                  "stable, project-unique id such as 'birdy.intro'.");
            if (definition.LinearLines.Count > 0)
                Warn(issues, "", "It has both nodes and linear lines; the lines are ignored. Delete them.");

            // Ids.
            var byId = new Dictionary<string, DialogueNode>(StringComparer.Ordinal);
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null) { Error(issues, "", $"Node #{i} is missing."); continue; }
                if (string.IsNullOrWhiteSpace(node.Id)) Error(issues, "", $"Node #{i} has an empty id.");
                else if (byId.ContainsKey(node.Id)) Error(issues, node.Id, $"Duplicate node id (node #{i}).");
                else byId.Add(node.Id, node);
            }

            string start = definition.StartNodeId;
            if (string.IsNullOrEmpty(start)) Error(issues, "", "The start node id is empty.");
            else if (!byId.ContainsKey(start)) Error(issues, "", $"The start node '{start}' doesn't exist.");

            // Each node on its own.
            var optionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.Id)) continue;
                switch (node.Kind)
                {
                    case DialogueNodeKind.Line:
                        if (string.IsNullOrWhiteSpace(node.Text)) Warn(issues, node.Id, "The line has no text.");
                        CheckLink(issues, byId, node, node.Next, "next");
                        break;

                    case DialogueNodeKind.Branch:
                        if (node.Conditions.Count == 0)
                            Warn(issues, node.Id, "The branch has no conditions, so it always goes to next.");
                        CheckConditions(issues, byId, definition, node, node.Conditions);
                        CheckLink(issues, byId, node, node.Next, "next");
                        CheckLink(issues, byId, node, node.ElseNext, "else next");
                        break;

                    case DialogueNodeKind.Choice:
                        CheckChoice(issues, byId, definition, node, optionIds);
                        break;
                }
            }

            CheckReachabilityAndExits(issues, byId, nodes, start);
            CheckBranchLoops(issues, byId, nodes);
        }

        // ------------------------------------------------------------ the rules

        static void CheckChoice(List<DialogueIssue> issues, Dictionary<string, DialogueNode> byId,
                                DialogueDefinition definition, DialogueNode node, HashSet<string> optionIds)
        {
            var options = node.Options;
            if (options.Count == 0)
            {
                Error(issues, node.Id, "The choice has no options.");
                CheckLink(issues, byId, node, node.Next, "fallback");
                return;
            }
            if (options.Count > MaxOptions)
                Error(issues, node.Id, $"{options.Count} options; at most {MaxOptions} fit the list and the number keys.");

            optionIds.Clear();
            bool anyUnconditional = false;
            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                if (option == null) { Error(issues, node.Id, $"Option #{i} is missing."); continue; }
                if (string.IsNullOrWhiteSpace(option.Id)) Error(issues, node.Id, $"Option #{i} has an empty id.");
                else if (!optionIds.Add(option.Id)) Error(issues, node.Id, $"Duplicate option id '{option.Id}'.");
                if (string.IsNullOrWhiteSpace(option.Text)) Warn(issues, node.Id, $"Option '{option.Id}' has no text.");
                else if (option.Text.Length > MaxOptionLength)
                    Warn(issues, node.Id, $"Option '{option.Id}' is {option.Text.Length} characters; past " +
                                          $"{MaxOptionLength} it may not fit the answer's two lines.");
                if (option.IsUnconditional) anyUnconditional = true;
                CheckConditions(issues, byId, definition, node, option.Conditions);
                foreach (var effect in option.Effects)
                    if (string.IsNullOrWhiteSpace(effect.Key))
                        Warn(issues, node.Id, $"Option '{option.Id}' has an effect with no fact key; it does nothing.");
                CheckLink(issues, byId, node, option.Next, $"option '{option.Id}'");
                CheckNotUnderOldQuestion(issues, byId, node, option.Next, $"Option '{option.Id}'");
            }

            if (!anyUnconditional && string.IsNullOrEmpty(node.Next))
                Error(issues, node.Id, "Every option has conditions, so all of them could be hidden or " +
                                       "disabled, and there is no fallback (next): the talk would end with " +
                                       "nothing to pick. Add an option without conditions, or a fallback.");
            CheckLink(issues, byId, node, node.Next, "fallback");
        }

        /// <summary>A pick that goes straight to a choice with no prompt shows the new
        /// answers under the question just answered.</summary>
        static void CheckNotUnderOldQuestion(List<DialogueIssue> issues, Dictionary<string, DialogueNode> byId,
                                             DialogueNode from, string target, string what)
        {
            if (string.IsNullOrEmpty(target) || !byId.TryGetValue(target, out var next)) return;
            if (next.Kind != DialogueNodeKind.Choice || next.ShowsLine) return;
            Warn(issues, from.Id, $"{what} goes straight to choice '{target}', which has no prompt, so its " +
                                  "answers would appear under the question just answered. Give it a prompt, " +
                                  "or put a line before it.");
        }

        static void CheckLink(List<DialogueIssue> issues, Dictionary<string, DialogueNode> byId,
                              DialogueNode from, string target, string what)
        {
            if (string.IsNullOrEmpty(target) || byId.ContainsKey(target)) return;
            Error(issues, from.Id, $"The {what} link goes to '{target}', which doesn't exist.");
        }

        /// <summary>Fact conditions need a key; "chose" conditions about this
        /// conversation must name a choice node (and option) it has.</summary>
        static void CheckConditions(List<DialogueIssue> issues, Dictionary<string, DialogueNode> byId,
                                    DialogueDefinition definition, DialogueNode node,
                                    IReadOnlyList<FactCondition> conditions)
        {
            foreach (var condition in conditions)
            {
                if (condition.Kind is FactConditionKind.Chose or FactConditionKind.NotChose)
                {
                    bool here = string.IsNullOrEmpty(condition.DialogueId)
                                || string.Equals(condition.DialogueId, definition.Id, StringComparison.Ordinal);
                    if (!here) continue;
                    if (!byId.TryGetValue(condition.NodeId, out var target) || target.Kind != DialogueNodeKind.Choice)
                    {
                        Warn(issues, node.Id, $"A '{condition.Kind}' condition names '{condition.NodeId}', which " +
                                              "isn't a choice node in this conversation, so it never changes.");
                        continue;
                    }
                    if (string.IsNullOrEmpty(condition.ChoiceId)) continue;
                    bool found = false;
                    foreach (var option in target.Options)
                        if (option != null && option.Id == condition.ChoiceId) { found = true; break; }
                    if (!found)
                        Warn(issues, node.Id, $"A '{condition.Kind}' condition names option '{condition.ChoiceId}', " +
                                              $"which '{condition.NodeId}' doesn't have.");
                }
                else if (string.IsNullOrWhiteSpace(condition.Key))
                    Warn(issues, node.Id, $"A '{condition.Kind}' condition has no fact key.");
            }
        }

        /// <summary>
        /// Unreachable nodes, and reachable nodes from which the talk can never
        /// end (a loop with no way out). A link that is empty, or that goes
        /// nowhere (already an error), counts as an end. A choice's fallback
        /// counts only if some option has conditions, and a branch's else only
        /// if it has conditions: otherwise those links are never taken.
        /// </summary>
        static void CheckReachabilityAndExits(List<DialogueIssue> issues, Dictionary<string, DialogueNode> byId,
                                              IReadOnlyList<DialogueNode> nodes, string start)
        {
            var links = new List<string>();
            var reachable = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(start) && byId.ContainsKey(start))
            {
                var stack = new Stack<string>();
                stack.Push(start);
                reachable.Add(start);
                while (stack.Count > 0)
                {
                    Links(byId[stack.Pop()], links);
                    foreach (string target in links)
                        if (!string.IsNullOrEmpty(target) && byId.ContainsKey(target) && reachable.Add(target))
                            stack.Push(target);
                }
                foreach (var node in nodes)
                    if (node != null && !string.IsNullOrWhiteSpace(node.Id) && byId[node.Id] == node
                        && !reachable.Contains(node.Id))
                        Warn(issues, node.Id, "It can't be reached from the start node.");
            }

            var canEnd = NodesThatCanEnd(byId);
            List<string> trapped = null;
            foreach (var node in nodes)
                if (node != null && reachable.Contains(node.Id) && !canEnd.Contains(node.Id))
                    (trapped ??= new List<string>()).Add(node.Id);
            if (trapped != null)
                Error(issues, trapped[0], "These nodes loop with no way out, so the talk can never end from " +
                                          $"them and the player would be stuck with the world stopped: " +
                                          $"{string.Join(", ", trapped)}. Give the loop an exit (an option or " +
                                          "branch link that leads to an end).");
        }

        /// <summary>
        /// The ids of the nodes from which some path ends the conversation (an
        /// empty link, or one to a node that doesn't exist). Optimistic: a link
        /// behind a condition counts. A choice's fallback counts only if some
        /// option has conditions, a branch's else only if it has conditions.
        /// The runner asks this (through <see cref="DialogueDefinition.CanReachEnd"/>)
        /// so it never walks into a loop with no way out.
        /// </summary>
        public static HashSet<string> NodesThatCanEnd(IReadOnlyList<DialogueNode> nodes)
        {
            var byId = new Dictionary<string, DialogueNode>(StringComparer.Ordinal);
            if (nodes != null)
                foreach (var node in nodes)
                    if (node != null && !string.IsNullOrWhiteSpace(node.Id) && !byId.ContainsKey(node.Id))
                        byId.Add(node.Id, node);
            return NodesThatCanEnd(byId);
        }

        static HashSet<string> NodesThatCanEnd(Dictionary<string, DialogueNode> byId)
        {
            var links = new List<string>();
            var canEnd = new HashSet<string>(StringComparer.Ordinal);
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var pair in byId)
                {
                    if (canEnd.Contains(pair.Key)) continue;
                    Links(pair.Value, links);
                    foreach (string target in links)
                    {
                        if (string.IsNullOrEmpty(target) || !byId.ContainsKey(target) || canEnd.Contains(target))
                        {
                            canEnd.Add(pair.Key);
                            changed = true;
                            break;
                        }
                    }
                }
            }
            return canEnd;
        }

        /// <summary>Branches that lead into each other with nothing shown between
        /// would spin forever (the runner gives up and ends the talk).</summary>
        static void CheckBranchLoops(List<DialogueIssue> issues, Dictionary<string, DialogueNode> byId,
                                     IReadOnlyList<DialogueNode> nodes)
        {
            // 0 unvisited, 1 on the current path, 2 done.
            var state = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var node in nodes)
            {
                if (node == null || node.Kind != DialogueNodeKind.Branch || string.IsNullOrWhiteSpace(node.Id)) continue;
                if (BranchLoopFrom(node.Id, byId, state, out string at))
                {
                    Error(issues, at, "Branch nodes loop into each other with no line or choice in between.");
                    return;
                }
            }
        }

        static bool BranchLoopFrom(string id, Dictionary<string, DialogueNode> byId, Dictionary<string, int> state,
                                   out string at)
        {
            at = id;
            if (state.TryGetValue(id, out int s)) return s == 1;
            state[id] = 1;
            var node = byId[id];
            // A branch with no conditions never takes its else link.
            int links = node.Conditions.Count > 0 ? 2 : 1;
            for (int i = 0; i < links; i++)
            {
                string target = i == 0 ? node.Next : node.ElseNext;
                if (string.IsNullOrEmpty(target) || !byId.TryGetValue(target, out var next)
                    || next.Kind != DialogueNodeKind.Branch) continue;
                if (BranchLoopFrom(target, byId, state, out at)) return true;
            }
            state[id] = 2;
            return false;
        }

        /// <summary>The links the talk can actually take out of <paramref name="node"/>.</summary>
        static void Links(DialogueNode node, List<string> links)
        {
            links.Clear();
            switch (node.Kind)
            {
                case DialogueNodeKind.Line:
                    links.Add(node.Next);
                    break;
                case DialogueNodeKind.Branch:
                    links.Add(node.Next);
                    if (node.Conditions.Count > 0) links.Add(node.ElseNext);
                    break;
                case DialogueNodeKind.Choice:
                    bool fallbackPossible = node.Options.Count == 0;
                    foreach (var option in node.Options)
                    {
                        if (option == null) continue;
                        links.Add(option.Next);
                        if (!option.IsUnconditional) fallbackPossible = true;
                    }
                    if (fallbackPossible) links.Add(node.Next);
                    break;
            }
        }

        static void Error(List<DialogueIssue> issues, string node, string message)
            => issues.Add(new DialogueIssue(DialogueIssueSeverity.Error, node, message));

        static void Warn(List<DialogueIssue> issues, string node, string message)
            => issues.Add(new DialogueIssue(DialogueIssueSeverity.Warning, node, message));
    }
}
