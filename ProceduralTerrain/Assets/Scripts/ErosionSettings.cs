using System;
using UnityEngine;

[Serializable]
public class ErosionSettings
{
    [Min(1)]
    public int numDroplets = 50000;

    [Range(1, 100)]
    public int maxLifetime = 30;

    // 관성: 1이면 방향 고정, 0이면 경사에 즉시 반응
    [Range(0f, 1f)]
    public float inertia = 0.05f;

    // 토사 용량 계수: 클수록 물방울이 더 많은 토사를 운반
    [Range(1f, 20f)]
    public float sedimentCapacityFactor = 4f;

    // 토사 용량 최솟값: 평지에서도 최소한의 침식 발생
    [Range(0f, 1f)]
    public float minSedimentCapacity = 0.01f;

    // 토사를 얼마나 빠르게 퇴적시킬지
    [Range(0f, 1f)]
    public float depositSpeed = 0.3f;

    // 토사를 얼마나 빠르게 깎을지
    [Range(0f, 1f)]
    public float erodeSpeed = 0.3f;

    // 매 스텝마다 수량이 줄어드는 비율
    [Range(0f, 1f)]
    public float evaporateSpeed = 0.01f;

    // 속도 갱신에 사용되는 중력 계수
    [Range(1f, 20f)]
    public float gravity = 4f;

    // 물방울 생성 초기 속도
    [Range(0f, 5f)]
    public float startSpeed = 1f;

    // 물방울 생성 초기 수량
    [Range(0f, 5f)]
    public float startWater = 1f;

    // 침식이 퍼지는 반경 (부드러운 침식을 위해 주변 격자에 분산)
    [Range(1, 8)]
    public int erodeRadius = 3;
}
