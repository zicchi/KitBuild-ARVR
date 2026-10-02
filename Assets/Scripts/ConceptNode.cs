using UnityEngine;

/// <summary>
/// Yellow concept node — user drags its port → link node to set as source.
/// </summary>
public class ConceptNode : MapNode
{
    static readonly Color BASE_BG   = new Color(1f, 0.749f, 0.251f, 0.92f); // #FFBF40
    static readonly Color BASE_TEXT = new Color(0.07f, 0.07f, 0.07f, 1f);   // #111

    public void Setup(ConceptData data)
    {
        Init(data.cid, data.label, BASE_BG, BASE_TEXT);
    }

    protected override Color GetBaseColor() => BASE_BG;
}
