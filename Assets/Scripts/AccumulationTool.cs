using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AccumulationTool : MonoBehaviour
{
    [Header("Ссылки")]
    [SerializeField] private AccumulationSurface surface;
    [SerializeField] private Transform visual; // Optional sphere/hemisphere visual.

    [Header("Движение")]
    [Min(0f)] [SerializeField] private float moveSpeed = 5f;
    [Tooltip("Высота инструмента над исходной плоскостью поверхности.")]
    [Min(0f)] [SerializeField] private float centerHeight = 0f;

    [Header("Радиус инструмента")]
    [Min(0.05f)] [SerializeField] private float baseRadius = 2f;
    [Min(0f)] [SerializeField] private float radiusAmplitude = 0.75f;
    [Min(0f)] [SerializeField] private float radiusFrequency = 0.5f;

    [SerializeField] private AnimationCurve radiusCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Vector3 previousPosition;
    private float radiusClock;
    private float currentRadius;

    public float CurrentRadius => currentRadius;

    private void Start()
    {
        previousPosition = transform.position;
        UpdateRadius(0f);
    }

    private void Update()
    {
        if (surface == null) return;

        float dt = Time.deltaTime;

        // Получаем ввод с клавиатуры для движения по плоскости
        Vector3 input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));

        // Не позволяем диагональному движению быть быстрее обычного
        if (input.sqrMagnitude > 1f) input.Normalize();

        Vector3 next = transform.position + input * (moveSpeed * dt);

        // Ограничиваем движение границами поверхности
        next.x = Mathf.Clamp(next.x, -surface.SizeX * 0.5f, surface.SizeX * 0.5f);

        next.z = Mathf.Clamp(next.z, -surface.SizeZ * 0.5f, surface.SizeZ * 0.5f);

        // Фиксируем высоту инструмента относительно поверхности
        next.y = surface.transform.position.y + centerHeight;
        transform.position = next;

        // Обновляем радиус во времени
        radiusClock += dt;
        UpdateRadius(dt);

        // Space поднимает поверхность, Caps Lock создаёт углубление
        if (Input.GetKey(KeyCode.Space))
        {
            surface.DepositAlongPath(previousPosition, transform.position, currentRadius, dt, false);
        }
        else if (Input.GetKey(KeyCode.CapsLock))
        {
            surface.DepositAlongPath(previousPosition, transform.position, currentRadius, dt, true);
        }

        // Запоминаем позицию для расчёта траектории в следующем кадре
        previousPosition = transform.position;
        
        // Применяем накопленные изменения геометрии
        surface.ApplyMeshUpdate();
    }

    private void UpdateRadius(float unused)
    {
        // Повторяем цикл радиуса, чтобы он плавно менялся со временем
        float cycle = Mathf.Repeat(radiusClock * radiusFrequency, 1f);

        // AnimationCurve задаёт характер изменения радиуса
        float curveValue = radiusCurve != null ? radiusCurve.Evaluate(cycle) : Mathf.Sin(cycle * Mathf.PI * 2f) * 0.5f + 0.5f;

        // Меняем радиус вокруг базового значения в заданных пределах
        currentRadius = Mathf.Max(0.05f, baseRadius + (curveValue * 2f - 1f) * radiusAmplitude);

        if (visual != null)
        {
            // Масштабируем визуальный объект под текущий радиус
            visual.localScale = new Vector3(currentRadius * 2f, currentRadius, currentRadius * 2f);

            // Ставим визуальный объект над центром инструмента
            visual.position = new Vector3(transform.position.x, surface != null ? surface.transform.position.y : transform.position.y, transform.position.z);
        }
    }

    public void ResetSurface()
    {
        if (surface != null) surface.ResetSurface();
    }
}
