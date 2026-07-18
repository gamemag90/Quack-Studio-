namespace QuackStudio.SharedSimCore
{
    // Per ADR-0001: no UnityEngine dependency anywhere in this assembly.
    // Referenced by both the Unity client (asmdef) and the standalone
    // .NET verification CLI (replay-verifier.exe).
    public interface IDeterministicRng
    {
        void Seed(ulong seed, int algorithmVersion);
        uint NextUInt32();
        float NextFloat01();
    }
}
