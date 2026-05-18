using UnityEngine;

namespace Sclass.EffectsSystem.Demo
{
    public class KineticMirrorEffect : BaseEffect
    {
        protected override void OnApply(GameObject target)
        {
            // Подписываемся на шину событий получения урона
            GameplayEventBus.OnDamageProcessing += HandleDamage;
        }

        protected override void OnRemove(GameObject target)
        {
            // ОБЯЗАТЕЛЬНАЯ отписка для предотвращения утечек
            GameplayEventBus.OnDamageProcessing -= HandleDamage;
        }

        private void HandleDamage(DamageContext context)
        {
            // Если урон предназначается не нашему носителю эффекта — игнорируем
            if (context.Target != Target) return;

            // Если кто-то до нас уже отменил урон — ничего не делаем
            if (context.IsCancelled) return;

            // С вероятностью 35% срабатывает зеркало
            if (Random.value <= 0.35f)
            {
                // 1. Отменяем урон по нашему игроку
                context.IsCancelled = true;
                
                Debug.Log($"<color=cyan>KineticMirror:</color> Урон ({context.RawDamage}) отменен и направлен обратно!");

                // 2. Направляем урон обратно
                if (context.Sender != null && context.Sender != Target)
                {
                    // Проверяем, является ли источник роем (SwarmManager наносит групповой урон)
                    if (context.Sender.TryGetComponent(out SwarmManager swarm))
                    {
                        // Рой не имеет единого коллайдера, найдем ближайшего агента вокруг игрока
                        Collider[] hits = Physics.OverlapSphere(Target.transform.position, 5f);
                        foreach (var hit in hits)
                        {
                            if (hit.TryGetComponent(out AgentHealth agentHealth))
                            {
                                // Наносим урон конкретному агенту (умножаем на 10 для наглядности)
                                agentHealth.TakeDamage(context.RawDamage * 10f);
                                Debug.Log($"<color=cyan>KineticMirror:</color> Урон отразился в агента {hit.name}!");
                                break; // Отражаем только в одного агента
                            }
                        }
                    }
                    else
                    {
                        // Для обычных врагов (если у них будет метод TakeDamage)
                        // context.Sender.SendMessage("TakeDamage", context.RawDamage, SendMessageOptions.DontRequireReceiver);
                    }
                }
            }
        }
    }
}
