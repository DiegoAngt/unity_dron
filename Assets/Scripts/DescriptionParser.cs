using UnityEngine;
using System.Text.RegularExpressions;

public class DescriptionParser : MonoBehaviour
{
    public PersonDescriptor ParseTextDescription(string description)
    {
        PersonDescriptor descriptor = new PersonDescriptor();
        string descLower = description.ToLower();
        
        // Análisis de chaqueta
        if (ContainsAny(descLower, "chaqueta", "casaca", "abrigo", "jacket"))
        {
            descriptor.requireJacket = true;
            descriptor.jacketColor = ExtractColor(descLower);
        }
        else if (ContainsAny(descLower, "sin chaqueta", "no tiene chaqueta"))
        {
            descriptor.requireJacket = false;
        }
        
        // Análisis de tocado/casco
        if (ContainsAny(descLower, "casco", "constructionhelmet", "casco de construcción"))
        {
            descriptor.headgearType = HeadgearType.ConstructionHelmet;
            descriptor.headgearColor = ExtractColor(descLower);
        }
        else if (ContainsAny(descLower, "gorra", "cap", "visera"))
        {
            descriptor.headgearType = HeadgearType.Cap;
            descriptor.headgearColor = ExtractColor(descLower);
        }
        else if (ContainsAny(descLower, "sombrero", "hat", "gorro"))
        {
            descriptor.headgearType = HeadgearType.Hat;
            descriptor.headgearColor = ExtractColor(descLower);
        }
        else if (ContainsAny(descLower, "sin tocado", "no lleva nada en la cabeza"))
        {
            descriptor.headgearType = HeadgearType.None;
        }
        
        // Análisis de mochila
        if (ContainsAny(descLower, "mochila", "backpack", "morral"))
        {
            descriptor.requireBackpackSpecified = true;
            descriptor.hasBackpack = !ContainsAny(descLower, "sin mochila", "no tiene mochila");
        }
        
        Debug.Log($"Descripción parseada: {descriptor}");
        return descriptor;
    }
    
    private bool ContainsAny(string text, params string[] terms)
    {
        foreach (string term in terms)
        {
            if (text.Contains(term.ToLower()))
                return true;
        }
        return false;
    }
    
    private ItemColor ExtractColor(string text)
    {
        if (text.Contains("rojo") || text.Contains("red")) return ItemColor.Red;
        if (text.Contains("azul") || text.Contains("blue")) return ItemColor.Blue;
        if (text.Contains("verde") || text.Contains("green")) return ItemColor.Green;
        if (text.Contains("amarillo") || text.Contains("yellow")) return ItemColor.Yellow;
        if (text.Contains("naranja") || text.Contains("orange")) return ItemColor.Orange;
        if (text.Contains("negro") || text.Contains("black")) return ItemColor.Black;
        if (text.Contains("blanco") || text.Contains("white")) return ItemColor.White;
        if (text.Contains("gris") || text.Contains("gray")) return ItemColor.Gray;
        
        return ItemColor.Black; // Color por defecto
    }
}