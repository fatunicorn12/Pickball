using System;
using UnityEngine;

namespace Pickleball
{
    /// <summary>
    /// Generic ScriptableObject event channel. Systems hold a reference to the asset and
    /// subscribe/raise through it, so no two gameplay systems ever reference each other directly.
    /// </summary>
    public abstract class EventChannelSO<T> : ScriptableObject
    {
        public event Action<T> OnEventRaised;

        public void Raise(T value)
        {
            OnEventRaised?.Invoke(value);
        }
    }

    /// <summary>Parameterless signal channel (e.g. "serve requested", "swing performed").</summary>
    [CreateAssetMenu(menuName = "Pickleball/Events/Void Event Channel", fileName = "VoidEvent")]
    public class VoidEventChannelSO : ScriptableObject
    {
        public event Action OnEventRaised;

        public void Raise()
        {
            OnEventRaised?.Invoke();
        }
    }

    /// <summary>Raised by the ball on every surface/out-of-bounds contact.</summary>
    [CreateAssetMenu(menuName = "Pickleball/Events/Ball Contact Channel", fileName = "BallContactEvent")]
    public class BallContactEventChannelSO : EventChannelSO<BallContactInfo> { }

    /// <summary>Raised when a paddle strikes the ball, carrying the side that hit it.</summary>
    [CreateAssetMenu(menuName = "Pickleball/Events/Court Side Channel", fileName = "CourtSideEvent")]
    public class CourtSideEventChannelSO : EventChannelSO<CourtSide> { }

    /// <summary>Raised when the high-level game state changes.</summary>
    [CreateAssetMenu(menuName = "Pickleball/Events/Game State Channel", fileName = "GameStateEvent")]
    public class GameStateEventChannelSO : EventChannelSO<GameStateId> { }

    /// <summary>Raised whenever the scoreboard changes.</summary>
    [CreateAssetMenu(menuName = "Pickleball/Events/Score Channel", fileName = "ScoreEvent")]
    public class ScoreEventChannelSO : EventChannelSO<ScoreSnapshot> { }
}
