using System.Collections.Generic;

namespace Exuarch.Core.SharpC
{
    internal sealed record SharpRuntimeRoutine(string[] Deps, string[] Data, string Code);

    internal static class SharpRuntime
    {
        private const string RoutinePrintStr = """

            ; Prints the 0-terminated string at the address in A
            __print_str:
                    ENTER  1
                    STL    -1
            __ps_loop:
                    LDL    -1
                    LDX
                    TST
                    JEQ    __ps_done
                    OUT
                    LDL    -1
                    INC
                    STL    -1
                    JMP    __ps_loop
            __ps_done:
                    RET    2
            """;

        private const string RoutinePrintInt = """

            ; Prints A as a signed decimal number
            __print_int:
                    TST
                    JGE    __pu            ; not negative: print the digits
                    TAB
                    LDI    '-'
                    OUT
                    LDI    -32768
                    CMP
                    JEQ    __pi_min        ; -32768 has no positive twin
                    TBA
                    NEG
                    JMP    __pu
            __pi_min:
                    LEA    __s_min
                    JMP    __print_str
            """;

        private const string RoutinePu = """

            ; Prints A, 0 to 32767, in decimal: the digits in front first, by recursion
            __pu:
                    ENTER  1
                    STL    -1
                    TAB
                    LDI    10
                    DIV
                    TST
                    JEQ    __pu_last
                    CALL   __pu
            __pu_last:
                    LDL    -1
                    TAB
                    LDI    10
                    MOD
                    TAB
                    LDI    '0'
                    ADD
                    OUT
                    RET    2
            """;

        private const string RoutinePrintBool = """

            ; Prints A as True or False
            __print_bool:
                    TST
                    JEQ    __pb_false
                    LEA    __s_true
                    JMP    __print_str
            __pb_false:
                    LEA    __s_false
                    JMP    __print_str
            """;

        private const string RoutineStrlen = """

            ; A = the length of the 0-terminated string at the address in A
            __strlen:
                    ENTER  2
                    STL    -1
                    STL    -2
            __sl_loop:
                    LDL    -2
                    LDX
                    TST
                    JEQ    __sl_done
                    LDL    -2
                    INC
                    STL    -2
                    JMP    __sl_loop
            __sl_done:
                    LDL    -2
                    TAB
                    LDL    -1
                    SUB
                    RET    2
            """;

        private const string RoutineSar = """

            ; A = B >> A, shifting copies of the sign bit in, like C#'s >>
            __sar:
                    ENTER  2
                    STL    -1
                    TBA
                    STL    -2
                    TST
                    JLT    __sar_neg
                    LDL    -2
                    TAB
                    LDL    -1
                    SHR
                    RET    2
            __sar_neg:
                    NOT
                    TAB
                    LDL    -1
                    SHR
                    NOT
                    RET    2
            """;

        public static readonly Dictionary<string, SharpRuntimeRoutine> Routines = new Dictionary<string, SharpRuntimeRoutine>
        {
            ["__print_str"] = new SharpRuntimeRoutine(new string[0], new string[0], RoutinePrintStr),
            ["__print_int"] = new SharpRuntimeRoutine(new[] { "__pu", "__print_str" }, new[] { "__s_min" }, RoutinePrintInt),
            ["__pu"] = new SharpRuntimeRoutine(new string[0], new string[0], RoutinePu),
            ["__print_bool"] = new SharpRuntimeRoutine(new[] { "__print_str" }, new[] { "__s_true", "__s_false" }, RoutinePrintBool),
            ["__strlen"] = new SharpRuntimeRoutine(new string[0], new string[0], RoutineStrlen),
            ["__sar"] = new SharpRuntimeRoutine(new string[0], new string[0], RoutineSar),
        };

        public static readonly Dictionary<string, string> Data = new Dictionary<string, string>
        {
            ["__s_min"] = "__s_min:  .STRING \"32768\"",
            ["__s_true"] = "__s_true: .STRING \"True\"",
            ["__s_false"] = "__s_false: .STRING \"False\"",
        };

        public const string Heap = """

            static class __Heap
            {
                static int __heapTop;
                static int __freeList;
                static int __markTop;
                static int __markLimit;
                static bool __markOverflow;

                static bool __Below(int a, int b) => (a ^ 0x8000) < (b ^ 0x8000);

                static int __New(int size, int layout)
                {
                    if (__heapTop == 0) __heapTop = __heapStart();
                    if (size == 0) size = 1;
                    int p = __TakeFree(size);
                    if (p == 0) p = __Grow(size);
                    if (p == 0)
                    {
                        __Collect();
                        p = __TakeFree(size);
                        if (p == 0) p = __Grow(size);
                    }
                    if (p == 0)
                    {
                        Console.WriteLine();
                        Console.Write("Out of memory");
                        Machine.Halt();
                    }
                    __poke(p - 1, layout);
                    int end = p + size;
                    for (int a = p; a != end; a++) __poke(a, 0);
                    return p;
                }

                static int __NewArray(int length, int layout)
                {
                    if (length < 0)
                    {
                        Console.WriteLine();
                        Console.Write("Negative array size");
                        Machine.Halt();
                    }
                    int p = __New(length + 1, layout);
                    __poke(p, length);
                    return p;
                }

                static int __Grow(int size)
                {
                    int limit = __fp() - 512;
                    int p = __heapTop + 2;
                    if (!__Below(p, limit)) return 0;
                    if (__Below(limit - p, size)) return 0;
                    __poke(__heapTop, size);
                    __heapTop = p + size;
                    return p;
                }

                static int __TakeFree(int size)
                {
                    int previous = 0;
                    int p = __freeList;
                    while (p != 0)
                    {
                        int have = __peek(p - 2);
                        int next = __peek(p);
                        if (!__Below(have, size))
                        {
                            if (!__Below(have, size + 3))
                            {
                                int rest = p + size + 2;
                                __poke(rest - 2, have - size - 2);
                                __poke(rest - 1, 0);
                                __poke(rest, next);
                                __poke(p - 2, size);
                                next = rest;
                            }
                            if (previous == 0) __freeList = next; else __poke(previous, next);
                            return p;
                        }
                        previous = p;
                        p = next;
                    }
                    return 0;
                }

                static void __Collect()
                {
                    if (__heapTop == 0) __heapTop = __heapStart();
                    __markTop = __heapTop;
                    __markLimit = __fp() - 128;
                    __markOverflow = false;
                    for (int r = __roots(); __peek(r) != 0; r++) __MarkCandidate(__peek(__peek(r)));
                    int a = __fp();
                    while (true)
                    {
                        __MarkCandidate(__peek(a));
                        if (a == -1) break;
                        a++;
                    }
                    __DrainMarks();
                    while (__markOverflow)
                    {
                        __markOverflow = false;
                        __RescanMarked();
                    }
                    __Sweep();
                }

                static void __MarkCandidate(int w)
                {
                    int start = __heapStart();
                    if (__Below(w, start + 2) || !__Below(w, __heapTop)) return;
                    int b = start;
                    while (__Below(b, __heapTop))
                    {
                        int p = b + 2;
                        if (__Below(w, p)) return;
                        int end = p + __peek(b);
                        if (__Below(w, end))
                        {
                            if ((__peek(p - 1) & 0x7FFF) != 0) __Mark(p);
                            return;
                        }
                        b = end;
                    }
                }

                static void __Mark(int p)
                {
                    if (p == 0) return;
                    int layout = __peek(p - 1);
                    if ((layout & 0x8000) != 0) return;
                    __poke(p - 1, layout | 0x8000);
                    if (__Below(__markTop, __markLimit))
                    {
                        __poke(__markTop, p);
                        __markTop++;
                    }
                    else __markOverflow = true;
                }

                static void __DrainMarks()
                {
                    while (__markTop != __heapTop)
                    {
                        __markTop--;
                        __MarkChildren(__peek(__markTop));
                    }
                }

                static void __MarkChildren(int p)
                {
                    int layout = __peek(p - 1) & 0x7FFF;
                    if (layout == 2)
                    {
                        int length = __peek(p);
                        for (int i = 0; i != length; i++) __Mark(__peek(p + 1 + i));
                    }
                    else if (layout > 2)
                    {
                        int count = __peek(layout);
                        for (int j = 1; j <= count; j++) __Mark(__peek(p + __peek(layout + j)));
                    }
                }

                static void __RescanMarked()
                {
                    int b = __heapStart();
                    while (__Below(b, __heapTop))
                    {
                        int p = b + 2;
                        if ((__peek(p - 1) & 0x8000) != 0)
                        {
                            __MarkChildren(p);
                            __DrainMarks();
                        }
                        b = p + __peek(b);
                    }
                }

                static void __Sweep()
                {
                    __freeList = 0;
                    int tail = 0;
                    int beforeTail = 0;
                    int run = 0;
                    int b = __heapStart();
                    while (__Below(b, __heapTop))
                    {
                        int p = b + 2;
                        int size = __peek(b);
                        int layout = __peek(p - 1);
                        if ((layout & 0x8000) != 0)
                        {
                            __poke(p - 1, layout & 0x7FFF);
                            run = 0;
                        }
                        else if (run != 0) __poke(run - 2, __peek(run - 2) + size + 2);
                        else
                        {
                            __poke(p - 1, 0);
                            __poke(p, 0);
                            if (tail == 0) __freeList = p; else __poke(tail, p);
                            beforeTail = tail;
                            tail = p;
                            run = p;
                        }
                        b = p + size;
                    }
                    if (run != 0)
                    {
                        __heapTop = run - 2;
                        if (beforeTail == 0) __freeList = 0; else __poke(beforeTail, 0);
                    }
                }
            }

            """;
    }
}
