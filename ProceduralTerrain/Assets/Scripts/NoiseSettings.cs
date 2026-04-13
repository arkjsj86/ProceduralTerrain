using System;
using UnityEngine;

[Serializable]
public class NoiseSettings
{
    [Range(1, 8)]
    public int octaves = 4;

    [Range(0.0001f, 10f)]
    public float frequency = 1f;

    [Range(0f, 10f)]
    public float amplitude = 1f;

    // 옥타브가 거듭될수록 주파수에 곱해지는 배율
    [Range(1f, 4f)]
    public float lacunarity = 2f;

    // 옥타브가 거듭될수록 진폭에 곱해지는 배율
    [Range(0f, 1f)]
    public float persistence = 0.5f;

    public int seed = 0;
    public Vector2 offset = Vector2.zero;
}
