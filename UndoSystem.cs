using System.Collections.Generic;
using MosaicPuzzle;
using UnityEngine;

namespace Code.Board
{
    [System.Serializable]
    public struct TileMove
    {
        public Vector2Int position;
        public TileState fromState;
        public TileState toState;

        public TileMove(Vector2Int pos, TileState from, TileState to)
        {
            position = pos;
            fromState = from;
            toState = to;
        }
    }

    public class UndoSystem
    {
        private const int targetHistorySize = 500;
        private const int maxHistorySize = targetHistorySize * 2;
        private readonly Stack<TileMove> moveHistory;

        public UndoSystem()
        {
            moveHistory = new Stack<TileMove>(maxHistorySize);
        }

        public void RecordMove(Vector2Int position, TileState fromState, TileState toState)
        {
            // Don't record moves involving solved tiles
            if (fromState.IsSolved() || toState.IsSolved())
                return;

            // Don't record if there's no actual change
            if (fromState == toState)
                return;

            var move = new TileMove(position, fromState, toState);
            moveHistory.Push(move);

            // Only trim when we significantly exceed the limit to avoid frequent array operations
            if (moveHistory.Count > maxHistorySize)
            {
                // Convert to array, take only the most recent moves, and rebuild stack
                var moves = moveHistory.ToArray();
                moveHistory.Clear();
                
                // Add back only the most recent moves (trim to target size)
                for (int i = targetHistorySize - 1; i >= 0; i--)
                {
                    moveHistory.Push(moves[i]);
                }
            }
        }

        public TileMove? PeekLastMove()
        {
            if (moveHistory.Count == 0)
                return null;

            return moveHistory.Peek();
        }

        public TileMove? UndoLastMove()
        {
            if (moveHistory.Count == 0)
                return null;

            return moveHistory.Pop();
        }

        public bool CanUndo => moveHistory.Count > 0;

        public int MoveCount => moveHistory.Count;

        public void ClearHistory()
        {
            moveHistory.Clear();
        }

        public string GetHistoryDebugString(int maxMoves = 10)
        {
            if (moveHistory.Count == 0)
                return "No moves in history";

            var moves = moveHistory.ToArray();
            var result = new System.Text.StringBuilder();
            result.AppendLine($"Move History (showing last {Mathf.Min(maxMoves, moves.Length)} of {moves.Length}):");

            int startIndex = Mathf.Max(0, moves.Length - maxMoves);
            for (int i = startIndex; i < moves.Length; i++)
            {
                var move = moves[i];
                result.AppendLine($"  {i + 1}: {move.position} {move.fromState} -> {move.toState}");
            }

            return result.ToString();
        }
    }
}
