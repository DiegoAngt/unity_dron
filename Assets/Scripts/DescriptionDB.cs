using UnityEngine;

[CreateAssetMenu(fileName="DescriptionDB", menuName="SearchSim/DescriptionDB")]
public class DescriptionDB : ScriptableObject
{
    public PersonDescriptor[] pool;

    public string ToHumanText(PersonDescriptor d)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder("Persona ");
        sb.Append(d.requireJacket ? $"con chaqueta {d.jacketColor} " : "sin chaqueta ");
        if (d.headgearType == HeadgearType.None)
            sb.Append("y sin tocado ");
        else
        {
            string head = d.headgearType == HeadgearType.Cap ? "gorra"
                       : d.headgearType == HeadgearType.Hat ? "sombrero"
                       : "casco de construcción";
            sb.Append($"y {head} {d.headgearColor} ");
        }
        sb.Append(d.requireBackpackSpecified ? (d.hasBackpack ? "con mochila" : "sin mochila") : "(mochila no importa)");
        return sb.ToString().Trim();
    }
}
