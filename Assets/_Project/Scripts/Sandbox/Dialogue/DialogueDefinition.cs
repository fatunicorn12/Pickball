using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// A complete conversation: the speaker's name, and either a branching
    /// node graph or (the original, linear form) a list of lines.
    ///
    /// Content lives on an asset rather than on the NPC so writers can edit it
    /// without touching scenes, and so a setup menu can seed it once and then
    /// leave hand edits alone.
    ///
    /// <b>Graph.</b> <see cref="Nodes"/> are lines, choices and branches
    /// (<see cref="DialogueNode"/>) linked by stable string ids, entered at
    /// <see cref="StartNodeId"/>. <see cref="Id"/> names the conversation in the
    /// choice log, so it must be unique and must never change once shipped.
    ///
    /// <b>Compatibility.</b> A definition with no nodes plays its
    /// <c>lines</c> array exactly as before: the runner reads it as an implicit
    /// chain of line nodes (<c>line.0 → line.1 → … → end</c>). Nothing is
    /// migrated automatically; an asset changes form only when someone gives it
    /// nodes (by hand, through <b>⋮ ▸ Convert Lines To Nodes</b>, or through its
    /// seeding setup menu). Once it has nodes, its lines are ignored and the
    /// validator says so.
    ///
    /// <see cref="DialogueValidator"/> runs in <c>OnValidate</c> and in an
    /// EditMode test over every definition in the project.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Dialogue/Dialogue Definition", fileName = "Dialogue")]
    public class DialogueDefinition : ScriptableObject, ISerializationCallbackReceiver
    {
        /// <summary>Id prefix of the implicit line nodes a node-less definition plays.</summary>
        public const string LegacyNodePrefix = "line.";

        [Tooltip("Stable id of this conversation, unique in the project (e.g. birdy.intro). " +
                 "The choice log records picks under it, so never change it once shipped. " +
                 "Required once the conversation has nodes.")]
        [SerializeField] private string id = "";

        [Tooltip("Name shown on the dialogue box's name tab. Empty falls back to the NPC's " +
                 "GameObject name.")]
        [SerializeField] private string speakerName = "";

        [Header("Branching")]
        [Tooltip("Id of the node the conversation starts at (often a Branch that checks what " +
                 "the player did last time).")]
        [SerializeField] private string startNodeId = "";

        [Tooltip("Lines, choices and branches, linked by id. Order is only for reading; the " +
                 "links decide the flow. With any nodes here, Lines below are ignored.")]
        [SerializeField] private DialogueNode[] nodes = Array.Empty<DialogueNode>();

        [Header("Linear (used only while Nodes is empty)")]
        [Tooltip("Lines in the order they are shown. The talk button moves to the next one; " +
                 "the conversation ends after the last. Ignored once Nodes has entries.")]
        [SerializeField] private DialogueLine[] lines = Array.Empty<DialogueLine>();

        // Built on first use and dropped whenever the asset is deserialized
        // (load, Inspector edit, undo, CopySerialized), so it is never stale and
        // survives a script reload by being rebuilt.
        [NonSerialized] Dictionary<string, DialogueNode> _index;
        [NonSerialized] List<DialogueLine> _authoredLines;

        /// <summary>The conversation's stable id; may be empty for a linear one. Never null.</summary>
        public string Id => id ?? string.Empty;

        /// <summary>Name for the dialogue box's name tab; may be empty.</summary>
        public string SpeakerName => speakerName;

        /// <summary>True when the definition has nodes; false plays the linear lines.</summary>
        public bool HasGraph => nodes != null && nodes.Length > 0;

        /// <summary>Where the conversation starts: the authored start node, or
        /// <c>line.0</c> for a linear one with lines. Empty when there is nothing.</summary>
        public string StartNodeId => HasGraph ? startNodeId ?? string.Empty
                                   : lines != null && lines.Length > 0 ? LegacyNodePrefix + "0" : string.Empty;

        /// <summary>Whether a conversation can start: a graph with a start id, or at
        /// least one line. What <see cref="Talkable.CanTalk"/> checks.</summary>
        public bool IsPlayable => HasGraph ? !string.IsNullOrEmpty(startNodeId) : lines != null && lines.Length > 0;

        /// <summary>The authored nodes in order (empty for a linear definition). Never null.</summary>
        public IReadOnlyList<DialogueNode> Nodes => nodes ?? Array.Empty<DialogueNode>();

        /// <summary>The legacy linear lines, as authored. Never null.</summary>
        public IReadOnlyList<DialogueLine> LinearLines => lines ?? Array.Empty<DialogueLine>();

        /// <summary>
        /// Number of lines this conversation can say, in authoring order: the
        /// linear lines, or a graph's Line nodes and choice prompts. A definition
        /// with no lines and no nodes cannot be talked to.
        /// </summary>
        public int LineCount => AuthoredLines.Count;

        /// <summary>The authored line at <paramref name="index"/> (0-based, below
        /// <see cref="LineCount"/>), in the order of <see cref="LineCount"/>. In a
        /// linear definition this is the line the talk shows at that index.</summary>
        public DialogueLine GetLine(int index) => AuthoredLines[index];

        /// <summary>
        /// Finds a node by id: an authored node, or for a linear definition one
        /// of its implicit <c>line.N</c> nodes. The first of duplicate ids wins
        /// (the validator reports duplicates).
        /// </summary>
        public bool TryGetNode(string nodeId, out DialogueNode node)
        {
            node = null;
            return !string.IsNullOrEmpty(nodeId) && Index.TryGetValue(nodeId, out node);
        }

        /// <summary>
        /// Builds an unsaved linear definition in code, for tests and for setup
        /// menus that seed content before saving it with <c>AssetDatabase.CreateAsset</c>.
        /// The line array is copied, so the caller's array can be reused.
        /// </summary>
        public static DialogueDefinition CreateRuntime(string speakerName, params DialogueLine[] lines)
        {
            var definition = CreateInstance<DialogueDefinition>();
            definition.speakerName = speakerName ?? string.Empty;
            definition.lines = lines != null ? (DialogueLine[])lines.Clone() : Array.Empty<DialogueLine>();
            return definition;
        }

        /// <summary>
        /// Builds an unsaved branching definition in code (tests, seeding setup
        /// menus). The node array is copied.
        /// </summary>
        public static DialogueDefinition CreateRuntime(string speakerName, string conversationId, string startNodeId,
                                                       params DialogueNode[] nodes)
        {
            var definition = CreateInstance<DialogueDefinition>();
            definition.id = conversationId ?? string.Empty;
            definition.speakerName = speakerName ?? string.Empty;
            definition.startNodeId = startNodeId ?? string.Empty;
            definition.nodes = nodes != null ? (DialogueNode[])nodes.Clone() : Array.Empty<DialogueNode>();
            return definition;
        }

        // ------------------------------------------------------------ internals

        Dictionary<string, DialogueNode> Index
        {
            get
            {
                if (_index != null) return _index;
                var index = new Dictionary<string, DialogueNode>(StringComparer.Ordinal);
                if (HasGraph)
                {
                    foreach (var node in nodes)
                        if (node != null && !string.IsNullOrEmpty(node.Id) && !index.ContainsKey(node.Id))
                            index.Add(node.Id, node);
                }
                else
                {
                    // The implicit chain: line.0 → line.1 → … → end.
                    int count = lines != null ? lines.Length : 0;
                    for (int i = 0; i < count; i++)
                    {
                        string next = i + 1 < count ? LegacyNodePrefix + (i + 1) : string.Empty;
                        var node = DialogueNode.CreateLine(LegacyNodePrefix + i, lines[i].Text, lines[i].Emote, next);
                        index.Add(node.Id, node);
                    }
                }
                _index = index;
                return _index;
            }
        }

        List<DialogueLine> AuthoredLines
        {
            get
            {
                if (_authoredLines != null) return _authoredLines;
                var list = new List<DialogueLine>();
                if (HasGraph)
                {
                    foreach (var node in nodes)
                        if (node != null && node.ShowsLine) list.Add(node.Line);
                }
                else if (lines != null) list.AddRange(lines);
                _authoredLines = list;
                return _authoredLines;
            }
        }

        void ClearCaches()
        {
            _index = null;
            _authoredLines = null;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        void ISerializationCallbackReceiver.OnAfterDeserialize() => ClearCaches();

#if UNITY_EDITOR
        [NonSerialized] string _lastReport;

        void OnValidate()
        {
            ClearCaches();
            var issues = DialogueValidator.Validate(this);
            string report = issues.Count == 0 ? null : DialogueValidator.Describe(issues);
            if (report == _lastReport) return;
            _lastReport = report;
            // A warning, never an error: OnValidate runs on every keystroke in the
            // Inspector, and an error log would fail any test that loads the asset.
            if (report != null) Debug.LogWarning($"Dialogue '{name}': {report}", this);
        }

        /// <summary>
        /// Migration helper (⋮ menu on the asset): turns the linear lines into
        /// line nodes with the same text and emotes, chained in order, with the
        /// ids the runner already uses for them (<c>line.0</c>, <c>line.1</c>, …),
        /// and empties the lines. Undoable. Give the conversation an id after.
        /// </summary>
        [ContextMenu("Convert Lines To Nodes")]
        void ConvertLinesToNodes()
        {
            if (HasGraph || lines == null || lines.Length == 0)
            {
                Debug.LogWarning($"Dialogue '{name}': nothing to convert (it already has nodes, or no lines).", this);
                return;
            }
            UnityEditor.Undo.RecordObject(this, "Convert Lines To Nodes");
            var converted = new DialogueNode[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                string next = i + 1 < lines.Length ? LegacyNodePrefix + (i + 1) : string.Empty;
                converted[i] = DialogueNode.CreateLine(LegacyNodePrefix + i, lines[i].Text, lines[i].Emote, next);
            }
            nodes = converted;
            startNodeId = LegacyNodePrefix + "0";
            lines = Array.Empty<DialogueLine>();
            UnityEditor.EditorUtility.SetDirty(this);
            OnValidate();
        }
#endif
    }
}
