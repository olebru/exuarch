using System;
using System.Collections.Generic;

namespace Exuarch.Core
{
    // What A* searches: states numbered from 0 so their costs can be kept in arrays, the steps between them, an
    // estimate of the cost left that never overestimates it, and which states the search may end in.
    internal interface ISearchProblem<TState>
    {
        int StateCount { get; }
        int IndexOf(TState state);
        double Heuristic(TState state);
        // What it costs to end the search in this state, or null when it can not end there.
        double? ArrivalCost(TState state);
        // The states one step on and what each step costs, in the order they are to be tried.
        void Steps(TState state, List<(TState Next, double Cost)> steps);
    }

    // A* that keeps searching after it first reaches a goal, until nothing cheaper is left in the queue, since what it
    // costs to arrive differs from goal to goal. A state can be expanded again when a cheaper way to it turns up.
    internal static class AStar
    {
        // The cheapest path from the start to a goal, both included, or null when there is none.
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
