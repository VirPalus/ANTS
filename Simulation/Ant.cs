namespace ANTS;

public class Ant
{
    public float X;
    public float Y;
    public float Heading;
    public float StridePhase;
    public AntRole Role;

    public Ant(float x, float y, float heading, AntRole role)
    {
        X = x;
        Y = y;
        Heading = heading;
        StridePhase = 0f;
        Role = role;
    }
}
