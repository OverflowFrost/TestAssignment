using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class AccumulationSurface : MonoBehaviour
{
    [Header("Размер поверхности")]
    [Min(2)] [SerializeField] private int resolutionX = 160;
    [Min(2)] [SerializeField] private int resolutionZ = 160;
    [Min(0.1f)] [SerializeField] private float sizeX = 20f;
    [Min(0.1f)] [SerializeField] private float sizeZ = 20f;

    [Header("Изменение поверхности")]
    [Tooltip("Скорость вертикального роста в мировых единицах в секунду")]
    [Min(0f)] [SerializeField] private float accumulationSpeed = 0.8f;
    [Tooltip("Максимальное количество выборок траектории на кадр. Более высокие значения уменьшают разрывы при высокой скорости")]
    [Range(1, 256)] [SerializeField] private int maxSamplesPerFrame = 96;

    private Mesh mesh;
    private Vector3[] vertices;
    private int[] triangles;
    private Vector2[] uvs;

    // Храним высоту каждой вершины отдельно, чтобы постепенно менять её
    private float[] heights;

    // Расстояние между соседними вершинами по осям X и Z
    private float cellX, cellZ;

    // Показывает, нужно ли передать изменённые вершины в Mesh
    private bool dirty;

    public float SizeX => sizeX;
    public float SizeZ => sizeZ;

    public float AccumulationSpeed { get => accumulationSpeed; set => accumulationSpeed = Mathf.Max(0f, value); }
    public int MaxSamplesPerFrame { get => maxSamplesPerFrame; set => maxSamplesPerFrame = Mathf.Clamp(value, 1, 256); }

    private void Awake() => BuildMesh();

    private void OnValidate()
    {
        // Не допускаем некорректные размеры сетки и отрицательную скорость
        resolutionX = Mathf.Max(2, resolutionX);
        resolutionZ = Mathf.Max(2, resolutionZ);
        sizeX = Mathf.Max(0.1f, sizeX);
        sizeZ = Mathf.Max(0.1f, sizeZ);
        accumulationSpeed = Mathf.Max(0f, accumulationSpeed);
    }

    private void BuildMesh()
    {
        // Рассчитываем шаг сетки, чтобы равномерно распределить вершины
        cellX = sizeX / (resolutionX - 1);
        cellZ = sizeZ / (resolutionZ - 1);

        int count = resolutionX * resolutionZ;

        vertices = new Vector3[count];
        uvs = new Vector2[count];
        heights = new float[count];

        // Каждая прямоугольная ячейка состоит из двух треугольников
        triangles = new int[(resolutionX - 1) * (resolutionZ - 1) * 6];

        // Создаём плоскую сетку с равномерно расположенными вершинами
        for (int z = 0; z < resolutionZ; z++)
        for (int x = 0; x < resolutionX; x++)
        {
            int i = Index(x, z);

            vertices[i] = new Vector3(x * cellX - sizeX * 0.5f, 0f, z * cellZ - sizeZ * 0.5f);

            // UV-координаты нужны для правильного наложения текстуры
            uvs[i] = new Vector2((float)x / (resolutionX - 1), (float)z / (resolutionZ - 1));
        }

        int t = 0;

        for (int z = 0; z < resolutionZ - 1; z++)
        for (int x = 0; x < resolutionX - 1; x++)
        {
            int a = Index(x, z);
            int b = Index(x + 1, z);
            int c = Index(x, z + 1);
            int d = Index(x + 1, z + 1);

            // Разбиваем каждую ячейку на два треугольника
            triangles[t++] = a; 
            triangles[t++] = c; 
            triangles[t++] = b;

            triangles[t++] = b; 
            triangles[t++] = c; 
            triangles[t++] = d;
        }

        mesh = new Mesh { name = "Accumulated Material Mesh" };

        // При большом количестве вершин используем 32-битные индексы
        if (count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        // Сообщаем Unity, что геометрия будет часто изменяться
        mesh.MarkDynamic();

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;

        // Пересчитываем освещение и границы меша после создания сетки.
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    /// <summary>
    /// Добавляет материал в указанной позиции XZ в мировых координатах. Полусфера определяет локальную целевую высоту;
    /// Уже накопленное значение высоты никогда не уменьшается при смене инструмента или радиуса.
    /// </summary>
    public void Deposit(Vector3 worldCenter, float radius, float deltaTime, bool dig = false)
    {
        if (radius <= 0f || deltaTime <= 0f || accumulationSpeed <= 0f) return;

        // Переводим позицию инструмента из мировых координат
        // в локальные координаты поверхности
        Vector3 local = transform.InverseTransformPoint(worldCenter);

        float centerX = local.x + sizeX * 0.5f;
        float centerZ = local.z + sizeZ * 0.5f;

        float r2 = radius * radius;

        // Проверяем только вершины внутри области инструмента,
        // а не всю сетку. Это уменьшает количество вычислений
        int minX = Mathf.Max(0, Mathf.FloorToInt((centerX - radius) / cellX));
        int maxX = Mathf.Min(resolutionX - 1, Mathf.CeilToInt((centerX + radius) / cellX));
        int minZ = Mathf.Max(0, Mathf.FloorToInt((centerZ - radius) / cellZ));
        int maxZ = Mathf.Min(resolutionZ - 1, Mathf.CeilToInt((centerZ + radius) / cellZ));

        float growth = accumulationSpeed * deltaTime;

        for (int z = minZ; z <= maxZ; z++)
        {
            float dz = z * cellZ - centerZ;

            float dz2 = dz * dz;

            for (int x = minX; x <= maxX; x++)
            {
                float dx = x * cellX - centerX;
                float distance2 = dx * dx + dz2;

                // Пропускаем вершины за пределами радиуса
                if (distance2 >= r2) continue;

                // Формула полусферы: в центре высота максимальная,
                // а ближе к краю она плавно уменьшается до нуля
                float targetHeight = Mathf.Sqrt(r2 - distance2);

                int i = Index(x, z);

                if (dig)
                {
                    // При копании используем отрицательную высоту,
                    // чтобы вершины уходили ниже исходной плоскости
                    targetHeight = -targetHeight;

                    // Уменьшаем высоту постепенно, не проходя ниже
                    // целевой глубины кратера
                    if (heights[i] > targetHeight)
                    {
                        heights[i] = Mathf.Max(
                            targetHeight,
                            heights[i] - growth
                        );

                        vertices[i].y = heights[i];
                        dirty = true;
                    }
                }
                else
                {
                    // При накоплении поднимаем вершины постепенно,
                    // но не выше целевой формы полусферы
                    if (heights[i] < targetHeight)
                    {
                        heights[i] = Mathf.Min(
                            targetHeight,
                            heights[i] + growth
                        );

                        vertices[i].y = heights[i];
                        dirty = true;
                    }
                }
            }
        }
    }

    /// <summary>Выполняет выборку точек на всем пути между двумя позициями, чтобы при быстром перемещении не пропустить зону.</summary>
    public void DepositAlongPath(Vector3 from, Vector3 to, float radius, float deltaTime, bool dig = false) // добавление копания
    {
        // Выбираем расстояние между проверками меньше шага сетки,
        // чтобы при быстром движении не оставались пропуски
        float spacing = Mathf.Min(cellX, cellZ) * 0.5f;

        // Для расчёта пути учитываем только горизонтальное перемещение
        float distance = Vector3.Distance(
            new Vector3(from.x, 0f, from.z),
            new Vector3(to.x, 0f, to.z));

        // Ограничиваем число проверок, чтобы не перегружать кадр
        int samples = Mathf.Clamp(Mathf.CeilToInt(distance / Mathf.Max(spacing, 0.001f)), 1, maxSamplesPerFrame);

        // Делим время кадра между проверками, чтобы скорость
        // изменения поверхности не зависела от количества проверок
        float sampleDt = deltaTime / samples;

        // Проверяем промежуточные позиции между прошлым
        // и текущим положением инструмента
        for (int i = 0; i <= samples; i++)
            Deposit(Vector3.Lerp(from, to, (float)i / samples), radius, sampleDt, dig);
    }
    public void ApplyMeshUpdate()
    {
        if (!dirty) return;

        // Передаём изменённые вершины в меш только при необходимости
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        dirty = false;
    }

    public void ResetSurface()
    {
        // Обнуляем высоты и возвращаем поверхность в исходное состояние
        for (int i = 0; i < heights.Length; i++)
        {
            heights[i] = 0f;
            vertices[i].y = 0f;
        }
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        dirty = false;
    }

    // Преобразуем координаты X и Z в индекс одномерного массива
    private int Index(int x, int z) => z * resolutionX + x;
}
