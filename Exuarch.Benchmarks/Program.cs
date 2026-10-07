using System;
using System.Diagnostics;
using System.Linq;
using Exuarch.Core;

// How many clock ticks a second the simulator runs, for a few built in programs, at full speed (nothing recorded) and
// with every tick recorded as the Run view does below 200 Hz. Run it in Release:
//   dotnet run -c Release --project Exuarch.Benchmarks
// Each figure is the median of several timed runs after a warm up.
var cases = new (string Package, string Program, bool Record)[]
{
    ("RISC-16", "Colour gradient on the screen", false),
    ("CISC-16", "Recursive Fibonacci", false),
    ("IRQ-16", "Falling blocks", false),
    ("GPU-16", "A spinning cube", false),
    ("DSP-16", "Mandelbrot, 80 x 60", false),
    ("RISC-16", "Colour gradient on the screen", true),
    ("IRQ-16", "Falling blocks", true),
};
const int Ticks = 400_000;
const int Runs = 7;
foreach (var (packageName, programName, record) in cases)
{
    var package = BuiltInPackages.Get(packageName);
    var program = package.Programs.FirstOrDefault(p => p.Name == programName) ?? package.Programs[0];
    double Measure()
    {
        var machine = new Machine(package.Machine, program.Source) { RecordHistory = record };
        int ticks = record ? Ticks / 10 : Ticks;
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < ticks && !machine.IsHalted; i++) machine.SingleStep();
        return machine.Cycles / watch.Elapsed.TotalSeconds;
    }
    Measure();
    var rates = Enumerable.Range(0, Runs).Select(_ => Measure()).OrderBy(r => r).ToList();
    Console.WriteLine($"{packageName,-10} {program.Name,-32} {(record ? "recorded" : "fast"),-9} {rates[Runs / 2] / 1e6,7:0.000} MHz  (min {rates[0] / 1e6:0.000}, max {rates[^1] / 1e6:0.000})");
}
