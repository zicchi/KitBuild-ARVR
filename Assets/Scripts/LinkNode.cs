using UnityEngine;

/// <summary>
/// Gray link (relation) node — user drags its port → concept node to add target.
/// </summary>
public class LinkNode : MapNode
{
    static readonly Color BASE_BG   = new Color(0.878f, 0.878f, 0.878f, 0.92f); // #DEDEDE
    static readonly Color BASE_TEXT = new Color(0.07f, 0.07f,  0.07f,  1f);

    // LinkNode punya tombol "←" dan "✕"
    protected override bool HasBackButton    => true;
    protected override bool HasDeleteButton  => true;

    public void Setup(LinkData data)
    {
        Init(data.lid, data.label, BASE_BG, BASE_TEXT);
    }

    protected override Color GetBaseColor() => BASE_BG;
}
