
public enum PairColorType 
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
    public PairColorType[] coloum;
}
