using System;
using System.Collections.Generic;

namespace Exuarch.Core
{
    internal interface ISearchProblem<TState>
    {
        int StateCount { get; }
        int IndexOf(TState state);
        double Heuristic(TState state);
        double? ArrivalCost(TState state);
        void Steps(TState state, List<(TState Next, double Cost)> steps);
    }

    internal static class AStar
    {
        public static List<TState> Search<TState>(ISearchProblem<TState> problem, TState start)
        {
            int count = problem.StateCount;
            var cost = new double[count];
            var from = new int[count];
            var states = new TState[count];
            Array.Fill(cost, double.MaxValue);
            Array.Fill(from, -1);
            var queue = new PriorityQueue<TState, double>();
            var steps = new List<(TState Next, double Cost)>();
            int first = problem.IndexOf(start);
            cost[first] = 0;
            states[first] = start;
            queue.Enqueue(start, problem.Heuristic(start));
            int found = -1;
            double foundCost = double.MaxValue;
            while (queue.TryDequeue(out var state, out var priority))
            {
                if (priority >= foundCost) break;
                int index = problem.IndexOf(state);
                var arrival = problem.ArrivalCost(state);
                if (arrival.HasValue && cost[index] + arrival.Value < foundCost)
                {
                    foundCost = cost[index] + arrival.Value;
                    found = index;
                }
                steps.Clear();
                problem.Steps(state, steps);
                foreach (var (next, step) in steps)
                {
                    int to = problem.IndexOf(next);
                    if (cost[index] + step >= cost[to]) continue;
                    cost[to] = cost[index] + step;
                    from[to] = index;
                    states[to] = next;
                    queue.Enqueue(next, cost[to] + problem.Heuristic(next));
                }
            }
            return found < 0 ? null : PathTo(found, from, states);
        }

        private static List<TState> PathTo<TState>(int found, int[] from, TState[] states)
        {
            var path = new List<TState>();
            for (int s = found; s >= 0; s = from[s]) path.Add(states[s]);
            path.Reverse();
            return path;
        }
    }
}
