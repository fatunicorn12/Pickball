using UnityEngine;

namespace Pickleball
{
    /// <summary>Which half of the court something belongs to.</summary>
    public enum CourtSide
    {
        Player,
        AI
    }

    /// <summary>Logical zones the ball can land in. Mirrors the tags placed on court colliders.</summary>
    public enum CourtZone
    {
        Kitchen,
        CourtLeft,
        CourtRight,
        OutOfBounds,
        Net
    }

    /// <summary>Lifecycle of the ball within a single point.</summary>
    public enum BallState
    {
        Idle,
        InFlight,
        Bounced,
        Dead
    }

    /// <summary>High level game-flow states driven by the GameStateManager.</summary>
    public enum GameStateId
    {
        Serving,
        Rally,
        PointScored,
        GameOver
    }

    /// <summary>Payload emitted whenever the ball contacts a surface (bounce or out-of-bounds).</summary>
    public struct BallContactInfo
    {
        public Vector3 Position;
        public CourtSide Side;
        public CourtZone Zone;
        public bool InBounds;

        public BallContactInfo(Vector3 position, CourtSide side, CourtZone zone, bool inBounds)
        {
            Position = position;
            Side = side;
            Zone = zone;
            InBounds = inBounds;
        }
    }

    /// <summary>Immutable snapshot of the scoreboard, raised whenever the score changes.</summary>
    public struct ScoreSnapshot
    {
        public int PlayerScore;
        public int AiScore;
        public CourtSide Server;

        public ScoreSnapshot(int playerScore, int aiScore, CourtSide server)
        {
            PlayerScore = playerScore;
            AiScore = aiScore;
            Server = server;
        }
    }
}
