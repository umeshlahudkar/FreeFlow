
public enum DotType 
{
   None = 0,
   Red = 1,
   Blue,
   Yellow,
   Green
}


[System.Serializable]
public struct GridRow
{
    public DotType[] coloum;
}
