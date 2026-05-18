using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Синглтон-рендерер дебага оружия. Рисует прямо в Game View через GL.
/// Создай пустой GameObject "WeaponDebugger" и повесь этот скрипт.
/// Projectile вызывает WeaponDebugger.Instance.RegisterHit() при попадании.
/// </summary>
public class WeaponDebugger : MonoBehaviour
{
    public static WeaponDebugger Instance { get; private set; }

    [Header("Включить/Выключить")]
    public bool Enabled = true;

    [Header("Траектория пули")]
    [Tooltip("Рисовать линию от мушки до точки попадания.")]
    public bool ShowTrajectory = true;
    public Color TrajectoryColor = new Color(1f, 0.85f, 0.1f, 0.8f); // жёлтая
    [Tooltip("Как долго (сек) держится линия траектории.")]
    public float TrajectoryDuration = 2f;

    [Header("Маркер попадания")]
    [Tooltip("Рисовать X-маркер в точке попадания.")]
    public bool ShowImpactMarker = true;
    public Color ImpactColor = Color.red;
    public float ImpactMarkerSize = 0.2f;
    public float ImpactDuration = 3f;

    [Header("Нормаль поверхности")]
    [Tooltip("Рисовать стрелку нормали поверхности.")]
    public bool ShowNormal = true;
    public Color NormalColor = Color.cyan;
    public float NormalLength = 0.5f;

    [Header("Splash-радиус")]
    [Tooltip("Рисовать сферу сплеш-радиуса при взрыве.")]
    public bool ShowSplash = true;
    public Color SplashColor = new Color(1f, 0.4f, 0f, 0.5f); // оранжевая

    [Header("Урон")]
    [Tooltip("Показывать цифру урона в точке попадания (через OnGUI).")]
    public bool ShowDamageNumbers = true;
    public float DamageNumberDuration = 1.5f;

    // ── Данные для рендера ───────────────────────────────────────────────

    private struct DebugLine
    {
        public Vector3 From, To;
        public Color   Color;
        public float   ExpireTime;
    }

    private struct DamageNumber
    {
        public Vector3 WorldPos;
        public float   Damage;
        public string  TargetName;
        public float   ExpireTime;
        public float   StartTime;
    }

    private readonly List<DebugLine>    _lines   = new List<DebugLine>();
    private readonly List<DamageNumber> _numbers = new List<DamageNumber>();

    private Material _lineMaterial;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Создаём материал для GL (unlit, без Z-теста чтобы линии были видны поверх всего)
        _lineMaterial = new Material(Shader.Find("Hidden/Internal-Colored"))
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        _lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _lineMaterial.SetInt("_Cull",     (int)UnityEngine.Rendering.CullMode.Off);
        _lineMaterial.SetInt("_ZWrite",   0);
        _lineMaterial.SetInt("_ZTest",    (int)UnityEngine.Rendering.CompareFunction.Always); // видно сквозь стены
    }

    private void Update()
    {
        // Чистим просроченные данные
        float now = Time.time;
        _lines.RemoveAll(l => l.ExpireTime < now);
        _numbers.RemoveAll(n => n.ExpireTime < now);
    }

    // ── Публичный API (вызывается из Projectile) ─────────────────────────

    /// <summary>
    /// Зарегистрировать попадание пули.
    /// </summary>
    public void RegisterHit(
        Vector3 fromPos,
        Vector3 hitPoint,
        Vector3 hitNormal,
        string  targetName,
        float   damage,
        float   splashRadius = 0f)
    {
        if (!Enabled) return;

        float now = Time.time;

        // Линия траектории
        if (ShowTrajectory)
        {
            AddLine(fromPos, hitPoint, TrajectoryColor, TrajectoryDuration);
        }

        // X-маркер попадания
        if (ShowImpactMarker)
        {
            float s = ImpactMarkerSize;
            // Ось X в плоскости нормали
            Vector3 right   = Vector3.Cross(hitNormal, Vector3.up);
            if (right.sqrMagnitude < 0.01f) right = Vector3.Cross(hitNormal, Vector3.forward);
            right.Normalize();
            Vector3 up = Vector3.Cross(hitNormal, right).normalized;

            AddLine(hitPoint - right * s, hitPoint + right * s, ImpactColor, ImpactDuration);
            AddLine(hitPoint - up    * s, hitPoint + up    * s, ImpactColor, ImpactDuration);

            // Маленький крест перпендикулярно нормали (диагонали)
            AddLine(hitPoint + (-right + up) * s * 0.5f, hitPoint + (right - up) * s * 0.5f, ImpactColor, ImpactDuration);
            AddLine(hitPoint + (right  + up) * s * 0.5f, hitPoint + (-right - up) * s * 0.5f, ImpactColor, ImpactDuration);
        }

        // Нормаль поверхности
        if (ShowNormal)
        {
            Vector3 normalEnd = hitPoint + hitNormal * NormalLength;
            AddLine(hitPoint, normalEnd, NormalColor, ImpactDuration);
            // Стрелочка (две линии под углом)
            Vector3 side = Vector3.Cross(hitNormal, Vector3.up).normalized * 0.08f;
            if (side.sqrMagnitude < 0.001f) side = Vector3.right * 0.08f;
            AddLine(normalEnd, normalEnd - hitNormal * 0.12f + side,  NormalColor, ImpactDuration);
            AddLine(normalEnd, normalEnd - hitNormal * 0.12f - side,  NormalColor, ImpactDuration);
        }

        // Splash-сфера
        if (ShowSplash && splashRadius > 0f)
        {
            DrawWireSphereLines(hitPoint, splashRadius, SplashColor, ImpactDuration);
        }

        // Цифра урона
        if (ShowDamageNumbers && damage > 0f)
        {
            _numbers.Add(new DamageNumber
            {
                WorldPos   = hitPoint + hitNormal * 0.1f,
                Damage     = damage,
                TargetName = targetName,
                ExpireTime = now + DamageNumberDuration,
                StartTime  = now
            });
        }
    }

    // ── GL рендер ────────────────────────────────────────────────────────

    private void OnRenderObject()
    {
        if (!Enabled || _lines.Count == 0) return;

        _lineMaterial.SetPass(0);
        GL.PushMatrix();
        GL.Begin(GL.LINES);

        float now = Time.time;
        for (int i = 0; i < _lines.Count; i++)
        {
            DebugLine l = _lines[i];
            // Плавно гасим линию в конце жизни
            float t = 1f - Mathf.Clamp01((l.ExpireTime - now) / 0.5f);
            Color c = l.Color;
            c.a *= (1f - t);
            GL.Color(c);
            GL.Vertex(l.From);
            GL.Vertex(l.To);
        }

        GL.End();
        GL.PopMatrix();
    }

    // ── OnGUI для цифр урона ─────────────────────────────────────────────

    private GUIStyle _damageStyle;

    private void OnGUI()
    {
        if (!Enabled || !ShowDamageNumbers || _numbers.Count == 0) return;
        if (Camera.main == null) return;

        if (_damageStyle == null)
        {
            _damageStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }

        float now = Time.time;
        foreach (var n in _numbers)
        {
            // Позиция в мире → позиция на экране
            Vector3 screen = Camera.main.WorldToScreenPoint(n.WorldPos);
            if (screen.z < 0) continue; // за камерой

            float progress = (now - n.StartTime) / DamageNumberDuration;
            // Всплываем вверх
            screen.y += progress * 40f;
            // Конвертируем в GUI-координаты (Y перевёрнут)
            float guiY = Screen.height - screen.y;

            // Прозрачность: появляется быстро, гаснет в конце
            float alpha = Mathf.Clamp01(progress < 0.1f ? progress / 0.1f : 1f - (progress - 0.7f) / 0.3f);

            string damageStr = Mathf.RoundToInt(n.Damage).ToString();

            // Тень
            _damageStyle.normal.textColor = new Color(0, 0, 0, alpha * 0.7f);
            GUI.Label(new Rect(screen.x - 49, guiY - 19, 100, 40), damageStr, _damageStyle);

            // Основной текст
            bool isAgent = n.TargetName.Contains("Agent") || n.TargetName.Contains("agent");
            Color textColor = isAgent
                ? new Color(1f, 0.3f, 0.1f, alpha)   // оранжево-красный — урон по агентам
                : new Color(1f, 1f, 0.2f, alpha);     // жёлтый — урон по остальному
            _damageStyle.normal.textColor = textColor;
            GUI.Label(new Rect(screen.x - 50, guiY - 20, 100, 40), damageStr, _damageStyle);
        }
    }

    // ── Вспомогательные методы ───────────────────────────────────────────

    private void AddLine(Vector3 from, Vector3 to, Color color, float duration)
    {
        _lines.Add(new DebugLine
        {
            From       = from,
            To         = to,
            Color      = color,
            ExpireTime = Time.time + duration
        });
    }

    /// <summary>Рисует wireframe-сферу набором линий (3 круга по осям).</summary>
    private void DrawWireSphereLines(Vector3 center, float radius, Color color, float duration)
    {
        int segments = 20;
        for (int axis = 0; axis < 3; axis++)
        {
            Vector3 prev = Vector3.zero;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                Vector3 point = axis == 0
                    ? new Vector3(0,              Mathf.Cos(angle), Mathf.Sin(angle))
                    : axis == 1
                    ? new Vector3(Mathf.Cos(angle), 0,             Mathf.Sin(angle))
                    : new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);

                point = center + point * radius;
                if (i > 0) AddLine(prev, point, color, duration);
                prev = point;
            }
        }
    }
}
