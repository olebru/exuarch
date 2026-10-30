(function(){
// A JavaScript port of the parts of the ExuArch core that SHARP-16 uses:
// the two phase clock, the devices, the decoder ROM, the microcode checks and
// the assembler. It follows Exuarch.Core (Clocking.cs, Machine.cs, DecoderRom.cs,
// MicrocodeRules.cs, Assembler*.cs and the device classes) tick for tick.
(function (root) {
  "use strict";
  const MASK = 0xffff, SIGN = 0x8000;
  const Z = 1, C = 2, V = 4, N = 8, I = 16;
  const STATUS_VARIANTS = 32;

  // ---------------------------------------------------------------- device types
  const out = (port = "data") => ({ drives: port });
  const inp = (port = "data") => ({ reads: port });
  const internal = () => ({});
  const registerLines = { output: out(), load: inp(), reset: internal(), inc: internal(), dec: internal() };
  const memoryLines = { loadmar: inp(), outputmar: out(), output: out(), incmar: internal(), decmar: internal() };
  const aluLine = (drives) => ({ ...(drives ? { drives: "data" } : {}), writesConnection: "status" });

  const TYPES = {
    register: { lines: registerLines, params: [], connections: [] },
    statusRegister: { lines: registerLines, params: [], connections: [] },
    instructionRegister: { lines: { load: inp(), reset: internal() }, params: [], connections: [], active: true },
    clock: { lines: { disable: { halts: true } }, params: [], connections: [], ports: [], active: true },
    ram: { lines: { ...memoryLines, load: inp() }, params: ["size"], connections: [] },
    alu: {
      lines: { add: aluLine(true), sub: aluLine(true), cmp: aluLine(false), and: aluLine(true), orr: aluLine(true), eor: aluLine(true), lsl: aluLine(true), lsr: aluLine(true) },
      params: [], connections: ["a", "b", "status"],
    },
    mac: { lines: { loada: inp(), loadb: inp(), mul: internal(), mac: internal(), div: internal(), output: out() }, params: ["shift"], connections: [] },
    display: { lines: { load: inp(), clear: internal() }, params: ["columns", "rows"], connections: [] },
    framebuffer: { lines: { loadx: inp(), loady: inp(), plot: inp(), skip: internal(), clear: internal() }, params: [], connections: [] },
    keypad: { lines: { output: out() }, params: [], connections: [] },
  };

  // ---------------------------------------------------------------- devices
  class Bus {
    constructor(id) { this.id = id; this.data = 0; this.written = false; this.writer = null; }
    begin() { this.data = 0; this.written = false; this.writer = null; }
    put(value, who) {
      if (this.written) throw new Error(`Bus '${this.id}': ${this.writer} and ${who} both drive it in the same tick`);
      this.data = value & MASK; this.written = true; this.writer = who;
    }
  }

  class Register {
    constructor(id, bus) {
      this.id = id; this.bus = bus; this.data = 0; this.out = false; this.armed = 0;
      this.lines = {
        output: () => { this.out = true; },
        load: () => { this.armed |= 1; }, reset: () => { this.armed |= 2; },
        inc: () => { this.armed |= 4; }, dec: () => { this.armed |= 8; },
      };
    }
    drive() { if (this.out) { this.bus.put(this.data, this.id); this.out = false; } }
    latch() {
      const a = this.armed; this.armed = 0;
      if (a & 1) this.data = this.bus.data;
      if (a & 2) this.data = 0;
      if (a & 4) this.data = (this.data + 1) & MASK;
      if (a & 8) this.data = (this.data - 1) & MASK;
    }
  }

  class InstructionRegister {
    constructor(id, bus) {
      this.id = id; this.bus = bus; this.data = 0; this.ld = false; this.rst = false;
      this.lines = { load: () => { this.ld = true; }, reset: () => { this.rst = true; } };
    }
    drive() {}
    latch() {
      if (this.rst) this.data = 0;
      else if (this.ld) this.data = this.bus.data & MASK;
      else this.data = (this.data + 1) & MASK;
      this.ld = this.rst = false;
    }
  }

  class Clock {
    constructor(id) { this.id = id; this.halted = false; this.lines = { disable: () => { this.halted = true; } }; }
    drive() {} latch() {}
  }

  class Ram {
    constructor(id, bus, size) {
      this.id = id; this.bus = bus; this.size = size; this.cells = new Uint16Array(size); this.mar = 0;
      this.f = 0; // 1 loadmar 2 outputmar 4 output 8 incmar 16 decmar 32 load
      this.lines = {
        loadmar: () => { this.f |= 1; }, outputmar: () => { this.f |= 2; }, output: () => { this.f |= 4; },
        incmar: () => { this.f |= 8; }, decmar: () => { this.f |= 16; }, load: () => { this.f |= 32; },
      };
    }
    drive() {
      if (this.f & 4) this.bus.put(this.cells[this.mar], this.id);
      if (this.f & 2) this.bus.put(this.mar, this.id);
    }
    latch() {
      const f = this.f; this.f = 0;
      if (f & 32) this.cells[this.mar] = this.bus.data & MASK;
      if (f & 1) this.mar = this.bus.data % this.size;
      if (f & 8) this.mar = (this.mar + 1) % this.size;
      if (f & 16) this.mar = (this.mar + this.size - 1) % this.size;
    }
  }

  const ALU_OPS = {
    add: [true, (x, y) => { const s = x + y, r = s & MASK; return [r, (s > MASK ? C : 0) | (((x ^ r) & (y ^ r) & SIGN) ? V : 0)]; }],
    sub: [true, (x, y) => { const r = (x - y) & MASK; return [r, (x < y ? C : 0) | (((x ^ y) & (x ^ r) & SIGN) ? V : 0)]; }],
    cmp: [false, (x, y) => { const r = (x - y) & MASK; return [r, (x < y ? C : 0) | (((x ^ y) & (x ^ r) & SIGN) ? V : 0)]; }],
    and: [true, (x, y) => [x & y, 0]],
    orr: [true, (x, y) => [x | y, 0]],
    eor: [true, (x, y) => [x ^ y, 0]],
    lsl: [true, (x, y) => { const s = y & 15; return [(x << s) & MASK, s > 0 && ((x >> (16 - s)) & 1) ? C : 0]; }],
    lsr: [true, (x, y) => { const s = y & 15; return [x >> s, s > 0 && ((x >> (s - 1)) & 1) ? C : 0]; }],
  };
  class Alu {
    constructor(id, bus, a, b, status) {
      this.id = id; this.bus = bus; this.a = a; this.b = b; this.status = status; this.pending = null; this.pendingStatus = null;
      this.lines = {};
      for (const name of Object.keys(ALU_OPS)) this.lines[name] = () => {
        if (this.pending && this.pending !== name) throw new Error(`${id}: '${this.pending}' and '${name}' can not run in the same tick`);
        this.pending = name;
      };
    }
    drive() {
      if (!this.pending) return;
      const [drives, fn] = ALU_OPS[this.pending];
      let [r, st] = fn(this.a.data & MASK, this.b.data & MASK);
      st |= (r === 0 ? Z : 0) | ((r & SIGN) ? N : 0);
      if (drives) this.bus.put(r, this.id);
      this.pendingStatus = st; this.pending = null;
    }
    latch() { if (this.pendingStatus !== null) { this.status.data = this.pendingStatus; this.pendingStatus = null; } }
  }

  const clamp = (v, lo, hi) => (v < lo ? lo : v > hi ? hi : v);
  const signed = (w) => ((w & MASK) ^ SIGN) - SIGN;
  class Mac {
    constructor(id, bus, shift) {
      this.id = id; this.bus = bus; this.shift = shift; this.A = 0; this.B = 0; this.acc = 0; this.out = false; this.armed = 0; this.next = null;
      const start = (k) => () => { if (this.next && this.next !== k) throw new Error(`${id}: only one of mul, mac and div can run in a tick`); this.next = k; };
      this.lines = {
        loada: () => { this.armed |= 1; }, loadb: () => { this.armed |= 2; },
        mul: start("mul"), mac: start("mac"), div: start("div"), output: () => { this.out = true; },
      };
    }
    result() { return clamp(Math.floor(this.acc / 2 ** this.shift), -32768, 32767); }
    drive() { if (this.out) { this.bus.put(this.result(), this.id); this.out = false; } }
    latch() {
      const a = this.armed; this.armed = 0;
      if (a & 1) this.A = signed(this.bus.data);
      if (a & 2) this.B = signed(this.bus.data);
      if (this.next === "mul") this.acc = this.A * this.B;
      else if (this.next === "mac") this.acc = this.acc + this.A * this.B;
      else if (this.next === "div") {
        if (this.B === 0) this.acc = this.acc < 0 ? -2147483648 : 2147483647;
        else this.acc = Math.trunc((this.acc * 2 ** this.shift) / this.B); // C# long division truncates
      }
      this.acc = clamp(this.acc, -2147483648, 2147483647);
      this.next = null;
    }
  }

  class Display {
    constructor(id, bus, columns, rows) {
      this.id = id; this.bus = bus; this.columns = columns; this.rows = rows;
      this.cells = new Uint8Array(columns * rows).fill(32); this.cursor = 0; this.ld = false; this.clr = false; this.writes = 0;
      this.lines = { load: () => { this.ld = true; }, clear: () => { this.clr = true; } };
    }
    drive() {}
    latch() {
      if (this.clr) { this.cells.fill(32); this.cursor = 0; this.clr = false; this.changed = true; }
      if (this.ld) { this.put(this.bus.data & 0xff); this.ld = false; this.writes++; }
    }
    put(ch) {
      const cols = this.columns, len = this.cells.length;
      let row = Math.floor(Math.min(this.cursor, len - 1) / cols);
      if (ch === 10) { if (row === this.rows - 1) this.scroll(); else row++; this.cursor = row * cols; }
      else if (ch === 13) this.cursor = row * cols;
      else {
        if (this.cursor === len) { this.scroll(); this.cursor = (this.rows - 1) * cols; }
        this.cells[this.cursor++] = ch;
      }
    }
    scroll() { this.cells.copyWithin(0, this.columns); this.cells.fill(32, this.cells.length - this.columns); }
    text() {
      const lines = [];
      for (let r = 0; r < this.rows; r++) {
        let s = "";
        for (let c = 0; c < this.columns; c++) {
          const v = this.cells[r * this.columns + c];
          s += (v >= 32 && v <= 126) || v >= 160 ? String.fromCharCode(v) : " ";
        }
        lines.push(s);
      }
      return lines.join("\n");
    }
  }

  class Framebuffer {
    constructor(id, bus) {
      this.id = id; this.bus = bus; this.W = 640; this.H = 480; this.pixels = new Uint16Array(640 * 480);
      this.x = 0; this.y = 0; this.f = 0; this.writes = 0; this.dirty = null;
      this.lines = { loadx: () => { this.f |= 1; }, loady: () => { this.f |= 2; }, plot: () => { this.f |= 4; }, skip: () => { this.f |= 8; }, clear: () => { this.f |= 16; } };
    }
    drive() {}
    mark(l, t, r, b) {
      const d = this.dirty;
      this.dirty = d ? [Math.min(d[0], l), Math.min(d[1], t), Math.max(d[2], r), Math.max(d[3], b)] : [l, t, r, b];
    }
    latch() {
      const f = this.f; this.f = 0;
      if (f & 16) { this.pixels.fill(0); this.x = this.y = 0; this.mark(0, 0, 639, 479); }
      if (f & 1) this.x = this.bus.data % 640;
      if (f & 2) this.y = this.bus.data % 480;
      if (f & 4) { this.pixels[this.y * 640 + this.x] = this.bus.data; this.mark(this.x, this.y, this.x, this.y); this.writes++; this.advance(); }
      if (f & 8) this.advance();
    }
    advance() { if (++this.x === 640) { this.x = 0; this.y = (this.y + 1) % 480; } }
  }

  class Keypad {
    constructor(id, bus) {
      this.id = id; this.bus = bus; this.held = 0; this.unread = 0; this.out = false; this.read = false;
      this.lines = { output: () => { this.out = true; } };
    }
    press(k) { this.held |= k; this.unread |= k; }
    release(k) { this.held &= ~k; }
    drive() { if (this.out) { this.bus.put(this.held | this.unread, this.id); this.out = false; this.read = true; } }
    latch() { if (this.read) { this.unread = 0; this.read = false; } }
  }

  // ---------------------------------------------------------------- microcode
  const FLAG_BITS = { N, V, C, Z, I };
  function condition(when) {
    if (!when) return null;
    let tested = 0, expected = 0;
    for (const [name, value] of Object.entries(when)) {
      const bit = FLAG_BITS[name];
      if (!bit) throw new Error(`unknown flag '${name}'`);
      tested |= bit; if (value) expected |= bit;
    }
    return { tested, expected };
  }
  function stepsFor(instruction, status) {
    return instruction.steps.filter((s) => { const c = condition(s.when); return !c || (status & c.tested) === c.expected; });
  }
  function allInstructions(microcode) { return [microcode.fetch, ...(microcode.instructions || [])]; }

  function resolveSignal(machine, text) {
    const dot = text.indexOf(".");
    if (dot <= 0 || dot === text.length - 1 || text.indexOf(".", dot + 1) >= 0 || /\s/.test(text)) return { problem: `'${text}' is not a signal, write it as device.line` };
    const id = text.slice(0, dot), line = text.slice(dot + 1);
    const device = machine.devices.find((d) => d.id === id);
    if (!device) return { problem: `there is no device '${id}'` };
    const type = TYPES[device.type];
    if (!type) return { problem: `device type '${device.type}' is not supported by this simulator` };
    const info = type.lines[line];
    if (!info) return { problem: `a ${device.type} has no control line '${line}', it has ${Object.keys(type.lines).join(", ")}` };
    const portBus = (port) => (port === "data" ? device.bus : (device.buses || {})[port]);
    return { id, line, device, info, drivenBus: info.drives ? portBus(info.drives) : null, readBus: info.reads ? portBus(info.reads) : null };
  }

  // Mirrors DefinitionRules and MicrocodeRules for the device types above.
  function validate(machine) {
    const errors = [], warnings = [];
    const ids = new Set(), busIds = new Set(machine.buses.map((b) => b.id));
    for (const d of machine.devices) {
      if (!d.id) { errors.push("Every device needs an \"id\"."); continue; }
      if (d.id.includes(".")) errors.push(`Device '${d.id}': an id can not contain '.'.`);
      if (ids.has(d.id)) errors.push(`Device '${d.id}' is defined more than once.`);
      ids.add(d.id);
      const type = TYPES[d.type];
      if (!type) { errors.push(`Device '${d.id}': unknown type '${d.type}'.`); continue; }
      for (const p of Object.keys(d.parameters || {})) if (!type.params.includes(p)) errors.push(`Device '${d.id}': a ${d.type} has no parameter '${p}'.`);
      for (const c of Object.keys(d.connections || {})) if (!type.connections.includes(c)) errors.push(`Device '${d.id}': a ${d.type} has no connection '${c}'.`);
      for (const c of type.connections) if (!(d.connections || {})[c]) errors.push(`Device '${d.id}': needs connection '${c}'.`);
      if (type.ports && type.ports.length === 0) { if (d.bus) errors.push(`Device '${d.id}': a ${d.type} is on no bus.`); }
      else if (!d.bus) errors.push(`Device '${d.id}': needs a bus connection, set "bus".`);
      else if (!busIds.has(d.bus)) errors.push(`Device '${d.id}': port 'data' connects to unknown bus '${d.bus}'.`);
    }
    for (const d of machine.devices) for (const [c, target] of Object.entries(d.connections || {})) if (!ids.has(target)) errors.push(`Device '${d.id}': connection '${c}' refers to unknown device '${target}'.`);
    const dec = machine.decoder;
    if (!dec || !dec.status || !dec.instructionRegister) { errors.push("\"decoder\" with \"status\" and \"instructionRegister\" is required."); return { errors, warnings }; }
    const mc = dec.microcode;
    if (!mc || !mc.fetch) { errors.push("Microcode: a fetch routine is required."); return { errors, warnings }; }
    const halt = machine.halt;
    const ends = new Set([`${dec.instructionRegister}.reset`, `${dec.instructionRegister}.load`, ...(halt ? [`${halt}.disable`] : [])]);
    const mnemonics = new Set();
    const where = (ins, i) => (i == null ? `Microcode ${ins.mnemonic}` : `Microcode ${ins.mnemonic} step ${i + 1}`);
    for (const ins of allInstructions(mc)) {
      if (!ins.mnemonic || /\s/.test(ins.mnemonic)) errors.push(`${where(ins)}: mnemonic must be a single word.`);
      else if (mnemonics.has(ins.mnemonic)) errors.push(`${where(ins)}: '${ins.mnemonic}' is defined more than once.`);
      mnemonics.add(ins.mnemonic);
      if (!ins.steps || ins.steps.length === 0) { errors.push(`${where(ins)}: has no steps.`); continue; }
      if (ins.operands != null && ins.operandTypes && ins.operandTypes.length !== ins.operands) errors.push(`${where(ins)}: operand count and operand types differ.`);
      for (const t of ins.operandTypes || []) if (!["value", "address", "register"].includes(t)) errors.push(`${where(ins)}: unknown operand type '${t}'.`);
      if ((ins.operandTypes || []).includes("register")) errors.push(`${where(ins)}: has a register operand, but the machine has no registerFile.`);
      ins.steps.forEach((step, i) => {
        const texts = step.signals || [];
        const seen = new Set();
        for (const t of texts) { if (seen.has(t)) warnings.push(`${where(ins, i)}: '${t}' is listed more than once.`); seen.add(t); }
        const resolved = [...seen].map((t) => ({ text: t, ...resolveSignal(machine, t) }));
        for (const r of resolved) if (r.problem) errors.push(`${where(ins, i)}: ${r.problem}`);
        const drivers = {};
        for (const r of resolved) if (r.drivenBus) (drivers[r.drivenBus] = drivers[r.drivenBus] || []).push(r.text);
        for (const [bus, list] of Object.entries(drivers)) if (list.length > 1) errors.push(`${where(ins, i)}: ${list.join(" and ")} all drive bus '${bus}' in the same tick.`);
        for (const r of resolved) if (r.readBus && !drivers[r.readBus]) warnings.push(`${where(ins, i)}: ${r.text} reads bus '${r.readBus}', but nothing drives it in this step.`);
        for (const r of resolved) {
          if (!r.info || !r.info.writesConnection) continue;
          const target = (r.device.connections || {})[r.info.writesConnection];
          const clash = resolved.find((o) => o.id === target && o.info && !o.info.drives);
          if (clash) errors.push(`${where(ins, i)}: ${r.text} writes the flags into ${target}, and ${clash.text} writes ${target} too.`);
        }
        if (step.when) { try { condition(step.when); } catch (e) { errors.push(`${where(ins, i)}: ${e.message}`); } }
      });
      const reported = new Set();
      for (let status = 0; status < STATUS_VARIANTS; status++) {
        const variant = stepsFor(ins, status);
        const key = variant.map((s) => ins.steps.indexOf(s)).join(",");
        if (variant.length === 0 || reported.has(key)) continue;
        reported.add(key);
        const end = variant.findIndex((s) => (s.signals || []).some((t) => ends.has(t)));
        if (end < 0) warnings.push(`${where(ins, ins.steps.indexOf(variant[variant.length - 1]))}: (status ${status}) never resets or loads '${dec.instructionRegister}', so it runs on into the next instruction.`);
        else if (end < variant.length - 1) warnings.push(`${where(ins, ins.steps.indexOf(variant[end + 1]))}: (status ${status}) returns to fetch before its last step.`);
      }
    }
    return { errors, warnings };
  }

  class DecoderRom {
    constructor(microcode) {
      this.blocks = []; this.base = {}; let used = 0;
      for (const ins of allInstructions(microcode)) {
        const variants = []; let size = 1;
        for (let s = 0; s < STATUS_VARIANTS; s++) { const v = stepsFor(ins, s); variants.push(v); size = Math.max(size, v.length); }
        this.base[ins.mnemonic] = used;
        this.blocks.push({ ins, base: used, size, variants });
        used += size;
      }
      this.used = used;
      this.canonical = {};
      for (const ins of microcode.instructions) {
        this.canonical[ins.mnemonic] = ins;
        const up = ins.mnemonic.toUpperCase();
        if (!(up in this.canonical)) this.canonical[up] = ins;
      }
    }
    find(mnemonic) { return this.canonical[mnemonic] || this.canonical[mnemonic.toUpperCase()] || null; }
    locate(step) {
      for (const b of this.blocks) if (step >= b.base && step < b.base + b.size) return b;
      return null;
    }
    signals(status, step) {
      const b = this.locate(step);
      if (!b) return { block: null, signals: [] };
      const v = b.variants[status & 31], i = step - b.base;
      return { block: b, index: i, step: v[i] || null, signals: v[i] ? v[i].signals : [] };
    }
  }

  // ---------------------------------------------------------------- assembler
  const isIdStart = (c) => /[\p{L}_.]/u.test(c);
  const isIdPart = (c) => /[\p{L}\p{N}_]/u.test(c);
  const ESC = { n: 10, t: 9, "0": 0, "\\": 92, '"': 34, "'": 39 };

  function tokenize(line) {
    const tokens = []; let i = 0;
    const err = (start, msg) => ({ kind: "error", start, end: i, msg });
    const readQuoted = (q) => {
      const values = [];
      while (i < line.length) {
        const c = line[i++];
        if (c === q) return { values };
        if (c === "\\" && i < line.length) { const e = line[i++]; if (!(e in ESC)) return { error: `unknown escape '\\${e}'` }; values.push(ESC[e]); continue; }
        const code = c.charCodeAt(0);
        if (code > 255) return { error: `'${c}' is not a Latin-1 character` };
        values.push(code);
      }
      return { error: `missing closing ${q}` };
    };
    while (i < line.length) {
      const c = line[i];
      if (c === " " || c === "\t") { i++; continue; }
      const start = i;
      if (c === ";") { tokens.push({ kind: "comment", start, end: line.length, text: line.slice(i) }); break; }
      if (c === ",") { i++; tokens.push({ kind: "comma", start, end: i }); continue; }
      if (c === '"') { i++; const r = readQuoted('"'); tokens.push(r.error ? err(start, r.error) : { kind: "string", start, end: i, values: r.values, text: line.slice(start, i) }); continue; }
      if (c === "#" || c === "'" || /[0-9]/.test(c) || (c === "-" && /[0-9]/.test(line[i + 1] || ""))) {
        let negative = false;
        if (line[i] === "#") i++; else if (line[i] === "-") { negative = true; i++; }
        if (line[i] === "'") {
          i++; const r = readQuoted("'");
          if (r.error) tokens.push(err(start, r.error));
          else if (r.values.length !== 1) tokens.push(err(start, "a character literal holds exactly one character"));
          else tokens.push({ kind: "number", start, end: i, value: r.values[0], text: line.slice(start, i) });
          continue;
        }
        const d = i; while (i < line.length && /[\p{L}\p{N}_]/u.test(line[i])) i++;
        const digits = line.slice(d, i), text = line.slice(start, i);
        let value = NaN;
        if (/^0x[0-9a-f]+$/i.test(digits)) value = parseInt(digits.slice(2), 16);
        else if (/^[0-9]+$/.test(digits)) value = parseInt(digits, 10);
        if (Number.isNaN(value)) tokens.push(err(start, `'${text}' is not a number, write 123, 0x7B, -5 or 'A'`));
        else if (negative && value > 32768) tokens.push(err(start, `'${text}' does not fit in 16 bits, the lowest is -32768`));
        else tokens.push({ kind: "number", start, end: i, value: negative ? -value & MASK : value, raw: value, text });
        continue;
      }
      if (isIdStart(c)) {
        i++; while (i < line.length && isIdPart(line[i])) i++;
        if (line[i] === ":") { i++; tokens.push({ kind: "label", start, end: i, name: line.slice(start, i - 1) }); }
        else tokens.push({ kind: "ref", start, end: i, name: line.slice(start, i), text: line.slice(start, i) });
        continue;
      }
      while (i < line.length && " \t,;".indexOf(line[i]) < 0) i++;
      tokens.push(err(start, `unexpected '${line.slice(start, i)}'`));
    }
    return tokens;
  }

  function assemble(source, rom, memorySize = 65536) {
    const diagnostics = [];
    const lines = source.split(/\r\n|\r|\n/);
    const statements = [];
    lines.forEach((text, index) => {
      const lineNumber = index + 1;
      const tokens = tokenize(text).filter((t) => t.kind !== "comment");
      for (const t of tokens) if (t.kind === "error") diagnostics.push({ severity: "error", line: lineNumber, message: t.msg });
      let k = 0, label = null;
      if (tokens[k] && tokens[k].kind === "label") label = tokens[k++];
      const head = tokens[k];
      if (!head) { statements.push({ lineNumber, text, label, kind: "empty", size: 0 }); return; }
      if (head.kind !== "ref") { diagnostics.push({ severity: "error", line: lineNumber, message: `expected a mnemonic or a directive, not '${text.slice(head.start, head.end)}'` }); statements.push({ lineNumber, text, label, kind: "empty", size: 0 }); return; }
      const operands = []; let expectOperand = true;
      for (let j = k + 1; j < tokens.length; j++) {
        const t = tokens[j];
        if (t.kind === "comma") { if (expectOperand) diagnostics.push({ severity: "error", line: lineNumber, message: "missing operand before ','" }); expectOperand = true; continue; }
        if (t.kind === "error") continue;
        if (!expectOperand) diagnostics.push({ severity: "error", line: lineNumber, message: `expected ',' before '${text.slice(t.start, t.end)}'` });
        if (t.kind === "label") diagnostics.push({ severity: "error", line: lineNumber, message: `a label can only start a line` });
        operands.push(t); expectOperand = false;
      }
      if (head.name.startsWith(".")) {
        const dir = head.name.toUpperCase();
        if (dir !== ".DATA" && dir !== ".STRING" && dir !== ".WORD" && dir !== ".BYTE") {
          diagnostics.push({ severity: "error", line: lineNumber, message: `unknown directive '${head.name}', use .DATA or .STRING` });
          statements.push({ lineNumber, text, label, kind: "empty", size: 0 }); return;
        }
        const size = operands.reduce((n, o) => n + (o.kind === "string" ? o.values.length : 1), 0) + (dir === ".STRING" ? 1 : 0);
        statements.push({ lineNumber, text, label, kind: "data", directive: dir, operands, size });
        return;
      }
      const ins = rom.find(head.name);
      if (!ins) { diagnostics.push({ severity: "error", line: lineNumber, message: `unknown instruction '${head.name}'` }); statements.push({ lineNumber, text, label, kind: "empty", size: 0 }); return; }
      const count = ins.operands != null ? ins.operands : (ins.operandTypes || []).length;
      if (operands.length !== count) diagnostics.push({ severity: "error", line: lineNumber, message: `${ins.mnemonic} takes ${count} operand${count === 1 ? "" : "s"}, not ${operands.length}` });
      statements.push({ lineNumber, text, label, kind: "instruction", ins, operands, size: 1 + count });
    });
    const labels = {}; let size = 0;
    for (const s of statements) {
      if (s.label) { if (s.label.name in labels) diagnostics.push({ severity: "error", line: s.lineNumber, message: `label '${s.label.name}' is defined more than once` }); else labels[s.label.name] = size; }
      s.address = size; size += s.size;
    }
    const cells = [], listing = [];
    for (const s of statements) {
      const start = cells.length;
      const encode = (o, type) => {
        if (o.kind === "number") { cells.push(o.value & MASK); return; }
        if (o.kind === "string") { if (s.kind !== "data") diagnostics.push({ severity: "error", line: s.lineNumber, message: "a string is only allowed in .DATA and .STRING" }); cells.push(...o.values); return; }
        if (o.kind === "ref") {
          if (!(o.name in labels)) { diagnostics.push({ severity: "error", line: s.lineNumber, message: `unknown label '${o.name}'` }); cells.push(0); return; }
          if (type === "value") diagnostics.push({ severity: "warning", line: s.lineNumber, message: `${s.ins.mnemonic} takes a value here, and '${o.name}' is the address of a label` });
          cells.push(labels[o.name]);
        }
      };
      if (s.kind === "instruction") {
        cells.push(rom.base[s.ins.mnemonic]);
        s.operands.forEach((o, i) => encode(o, (s.ins.operandTypes || [])[i]));
      } else if (s.kind === "data") {
        s.operands.forEach((o) => encode(o, null));
        if (s.directive === ".STRING") cells.push(0);
      }
      while (cells.length < start + s.size) cells.push(0);
      if (s.kind !== "empty") listing.push({ line: s.lineNumber, address: s.address, cells: cells.slice(start, start + s.size), instruction: s.kind === "instruction" ? s.ins.mnemonic : null });
    }
    if (cells.length > memorySize) diagnostics.push({ severity: "error", line: 1, message: `the program is ${cells.length} cells, but program memory only holds ${memorySize}` });
    diagnostics.sort((a, b) => a.line - b.line);
    return { cells, labels, listing, diagnostics, errors: diagnostics.filter((d) => d.severity === "error") };
  }

  // ---------------------------------------------------------------- the machine
  class Machine {
    constructor(definition, source) {
      const check = validate(definition);
      if (check.errors.length) throw Object.assign(new Error(check.errors.join("\n")), { errors: check.errors });
      this.warnings = check.warnings;
      this.definition = definition;
      this.buses = {};
      for (const b of definition.buses) this.buses[b.id] = new Bus(b.id);
      this.devices = {}; this.order = [];
      const build = (d) => {
        if (this.devices[d.id]) return this.devices[d.id];
        const bus = this.buses[d.bus], p = d.parameters || {}, c = d.connections || {};
        const ref = (id) => build(definition.devices.find((x) => x.id === id));
        let dev;
        switch (d.type) {
          case "register": case "statusRegister": dev = new Register(d.id, bus); break;
          case "instructionRegister": dev = new InstructionRegister(d.id, bus); break;
          case "clock": dev = new Clock(d.id); break;
          case "ram": dev = new Ram(d.id, bus, p.size ?? 4096); break;
          case "alu": dev = new Alu(d.id, bus, ref(c.a), ref(c.b), ref(c.status)); break;
          case "mac": dev = new Mac(d.id, bus, p.shift ?? 8); break;
          case "display": dev = new Display(d.id, bus, p.columns ?? 16, p.rows ?? 4); break;
          case "framebuffer": dev = new Framebuffer(d.id, bus); break;
          case "keypad": dev = new Keypad(d.id, bus); break;
          default: throw new Error(`unsupported device type ${d.type}`);
        }
        dev.active = !!TYPES[d.type].active;
        this.devices[d.id] = dev;
        return dev;
      };
      for (const d of definition.devices) build(d);
      this.order = definition.devices.map((d) => this.devices[d.id]);
      this.busList = Object.values(this.buses);
      this.ir = this.devices[definition.decoder.instructionRegister];
      this.status = this.devices[definition.decoder.status];
      this.halt = definition.halt ? this.devices[definition.halt] : null;
      this.memory = this.devices[definition.programMemory];
      this.rom = new DecoderRom(definition.decoder.microcode);
      this.plans = [];
      this.cycles = 0;
      this.instructions = 0;
      this.currentAddress = null;
      this.program = assemble(source || "", this.rom, this.memory ? this.memory.size : 65536);
      if (this.program.errors.length) throw Object.assign(new Error(this.program.errors.map((e) => `line ${e.line}: ${e.message}`).join("\n")), { errors: this.program.errors });
      this.memory.cells.set(this.program.cells);
    }
    get halted() { return this.halt ? this.halt.halted : false; }
    plan(status, step) {
      const row = this.plans[status] || (this.plans[status] = []);
      let plan = row[step];
      if (plan) return plan;
      const { signals, block } = this.rom.signals(status, step);
      const enables = signals.map((t) => {
        const dot = t.indexOf(".");
        const dev = this.devices[t.slice(0, dot)];
        return dev.lines[t.slice(dot + 1)];
      });
      const enabled = new Set(signals.map((t) => t.slice(0, t.indexOf("."))));
      const clocked = this.order.filter((d) => d.active || enabled.has(d.id));
      const irId = this.definition.decoder.instructionRegister, memId = this.definition.programMemory;
      plan = { enables, clocked, mnemonic: block ? block.ins.mnemonic : null,
        loadsInstruction: signals.includes(`${irId}.load`) && signals.includes(`${memId}.output`) };
      row[step] = plan;
      return plan;
    }
    step() {
      if (this.halted) return false;
      const plan = this.plan(this.status.data & 15, this.ir.data);
      const enables = plan.enables;
      for (let i = 0; i < enables.length; i++) enables[i]();
      const buses = this.busList;
      for (let i = 0; i < buses.length; i++) buses[i].begin();
      const clocked = plan.clocked;
      for (let i = 0; i < clocked.length; i++) clocked[i].drive();
      for (let i = 0; i < clocked.length; i++) clocked[i].latch();
      this.cycles++;
      if (plan.loadsInstruction) { this.currentAddress = this.memory.mar; this.instructions++; }
      return true;
    }
    run(maxTicks) {
      let n = 0;
      while (n < maxTicks && !this.halted) { this.step(); n++; }
      return n;
    }
    device(id) { return this.devices[id]; }
  }

  const api = { Machine, validate, assemble, DecoderRom, TYPES };
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  else root.ExuSim = api;
})(typeof window !== "undefined" ? window : globalThis);

})();
(function(){
// SharpC: a compiler from a small subset of C# to SHARP-16 assembly.
(function (root) {
  "use strict";

  class CompileError extends Error {
    constructor(message, line, col) { super(message); this.line = line; this.col = col; }
  }
  const fail = (msg, node) => { throw new CompileError(msg, node ? node.line : 0, node ? node.col : 0); };
  const s16 = (x) => (((x | 0) & 0xffff) ^ 0x8000) - 0x8000;

  // ================================================================ lexer
  const KEYWORDS = new Set(["using", "namespace", "class", "struct", "static", "public", "private", "internal", "protected", "readonly", "partial", "sealed", "const",
    "void", "int", "short", "bool", "char", "string", "var", "if", "else", "while", "do", "for", "foreach", "in", "switch", "case", "default",
    "break", "continue", "return", "true", "false", "new", "null", "this", "long", "uint", "ushort", "byte", "sbyte", "float", "double", "decimal", "object", "ulong"]);
  const OPS = [">>>=", "<<=", ">>=", ">>>", "&&", "||", "==", "!=", "<=", ">=", "<<", ">>", "++", "--", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "=>",
    "+", "-", "*", "/", "%", "&", "|", "^", "!", "~", "<", ">", "=", "?", ":", ";", ",", ".", "(", ")", "{", "}", "[", "]"];
  const ESCAPES = { n: 10, t: 9, r: 13, "0": 0, "\\": 92, "'": 39, '"': 34, a: 7, b: 8, f: 12, v: 11 };

  function lex(src, baseLine = 1, baseCol = 1) {
    const tokens = [];
    let i = 0, line = baseLine, col = baseCol;
    const adv = (n = 1) => { for (let k = 0; k < n; k++) { if (src[i] === "\n") { line++; col = 1; } else col++; i++; } };
    const err = (msg) => { throw new CompileError(msg, line, col); };
    function escape() {
      // at a backslash; returns the char code
      adv();
      const e = src[i];
      if (e === "u" || e === "x") {
        adv();
        let hex = "";
        while (hex.length < 4 && /[0-9a-fA-F]/.test(src[i] || "")) { hex += src[i]; adv(); }
        if (!hex) err(`bad escape '\\${e}'`);
        return parseInt(hex, 16);
      }
      if (!(e in ESCAPES)) err(`unknown escape '\\${e}'`);
      adv();
      return ESCAPES[e];
    }
    while (i < src.length) {
      const c = src[i];
      if (c === "\n" || c === " " || c === "\t" || c === "\r") { adv(); continue; }
      if (c === "/" && src[i + 1] === "/") { while (i < src.length && src[i] !== "\n") adv(); continue; }
      if (c === "/" && src[i + 1] === "*") {
        adv(2);
        while (i < src.length && !(src[i] === "*" && src[i + 1] === "/")) adv();
        if (i >= src.length) err("comment is not closed");
        adv(2); continue;
      }
      if (c === "#") { while (i < src.length && src[i] !== "\n") adv(); continue; } // #region and friends
      const start = { line, col };
      if (/[A-Za-z_]/.test(c) || (c === "@" && /[A-Za-z_]/.test(src[i + 1] || ""))) {
        if (c === "@") adv();
        let s = "";
        while (i < src.length && /[A-Za-z0-9_]/.test(src[i])) { s += src[i]; adv(); }
        tokens.push({ t: KEYWORDS.has(s) && c !== "@" ? "kw" : "id", v: s, ...start });
        continue;
      }
      if (/[0-9]/.test(c)) {
        let s = "";
        while (i < src.length && /[0-9A-Za-z_]/.test(src[i])) { s += src[i]; adv(); }
        const clean = s.replace(/_/g, "");
        let v;
        if (/^0[xX][0-9a-fA-F]+$/.test(clean)) v = parseInt(clean.slice(2), 16);
        else if (/^0[bB][01]+$/.test(clean)) v = parseInt(clean.slice(2), 2);
        else if (/^[0-9]+$/.test(clean)) v = parseInt(clean, 10);
        else if (/^[0-9]+[uUlL]+$/.test(clean)) err(`'${s}': only 16-bit int is supported, drop the suffix`);
        else err(`'${s}' is not a number`);
        if (v > 65535) err(`${s} does not fit in 16 bits: SHARP-16's int runs from -32768 to 32767 (0x0000 to 0xFFFF)`);
        tokens.push({ t: "num", v, raw: s, ...start });
        continue;
      }
      if (c === "'") {
        adv();
        let v;
        if (src[i] === "\\") v = escape(); else { if (src[i] === "'" || src[i] === "\n" || i >= src.length) err("empty character literal"); v = src.charCodeAt(i); adv(); }
        if (src[i] !== "'") err("a character literal holds one character");
        adv();
        tokens.push({ t: "char", v, ...start });
        continue;
      }
      if (c === '"' || (c === "@" && src[i + 1] === '"')) {
        const verbatim = c === "@";
        adv(verbatim ? 2 : 1);
        let s = "";
        for (;;) {
          if (i >= src.length || (!verbatim && src[i] === "\n")) err("string is not closed");
          if (src[i] === '"') { if (verbatim && src[i + 1] === '"') { s += '"'; adv(2); continue; } adv(); break; }
          if (!verbatim && src[i] === "\\") { s += String.fromCharCode(escape()); continue; }
          s += src[i]; adv();
        }
        tokens.push({ t: "str", v: s, ...start });
        continue;
      }
      if (c === "$" && src[i + 1] === '"') {
        adv(2);
        const parts = []; let text = "";
        for (;;) {
          if (i >= src.length || src[i] === "\n") err("interpolated string is not closed");
          const ch = src[i];
          if (ch === '"') { adv(); break; }
          if (ch === "\\") { text += String.fromCharCode(escape()); continue; }
          if (ch === "{" && src[i + 1] === "{") { text += "{"; adv(2); continue; }
          if (ch === "}" && src[i + 1] === "}") { text += "}"; adv(2); continue; }
          if (ch === "{") {
            if (text) { parts.push(text); text = ""; }
            adv();
            const eLine = line, eCol = col;
            let depth = 0, e = "";
            while (i < src.length && !(depth === 0 && src[i] === "}")) {
              if (src[i] === "\n") err("interpolated string is not closed");
              if (src[i] === "(" || src[i] === "[") depth++;
              if (src[i] === ")" || src[i] === "]") depth--;
              if (depth === 0 && (src[i] === ":" || src[i] === ",")) err("format and alignment specifiers in {…} are not supported");
              if (src[i] === '"' || src[i] === "'") err("put strings and characters in a variable before using them inside {…}");
              e += src[i]; adv();
            }
            adv();
            parts.push({ src: e, line: eLine, col: eCol });
            continue;
          }
          text += ch; adv();
        }
        if (text) parts.push(text);
        tokens.push({ t: "interp", parts, ...start });
        continue;
      }
      const op = OPS.find((o) => src.startsWith(o, i));
      if (!op) err(`unexpected '${c}'`);
      adv(op.length);
      tokens.push({ t: "op", v: op, ...start });
    }
    tokens.push({ t: "eof", v: "end of file", line, col });
    return tokens;
  }

  // ================================================================ parser
  const BASE_TYPES = new Set(["int", "short", "bool", "char", "string", "void", "var"]);
  const UNSUPPORTED_TYPES = new Set(["long", "uint", "ushort", "byte", "sbyte", "float", "double", "decimal", "object", "ulong"]);
  const MODIFIERS = new Set(["static", "public", "private", "internal", "protected", "readonly", "partial", "sealed"]);
  const ASSIGN_OPS = new Set(["=", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<=", ">>=", ">>>="]);
  const BINARY_LEVELS = [["||"], ["&&"], ["|"], ["^"], ["&"], ["==", "!="], ["<", ">", "<=", ">="], ["<<", ">>", ">>>"], ["+", "-"], ["*", "/", "%"]];

  class Parser {
    constructor(tokens, runtimeNames = false) {
      this.toks = tokens; this.p = 0; this.runtimeNames = runtimeNames;
      this.classNames = new Set();
      tokens.forEach((t, i) => { if (t.t === "kw" && t.v === "class" && tokens[i + 1] && tokens[i + 1].t === "id") this.classNames.add(tokens[i + 1].v); });
    }
    peek(n = 0) { return this.toks[Math.min(this.p + n, this.toks.length - 1)]; }
    next() { return this.toks[this.p++]; }
    is(v, n = 0) { const t = this.peek(n); return (t.t === "op" || t.t === "kw") && t.v === v; }
    accept(v) { if (this.is(v)) { return this.next(); } return null; }
    expect(v, what) {
      if (this.is(v)) return this.next();
      const t = this.peek();
      throw new CompileError(`expected '${v}'${what ? " " + what : ""} but found ${describe(t)}`, t.line, t.col);
    }
    ident(what = "a name") {
      const t = this.peek();
      if (t.t !== "id") throw new CompileError(`expected ${what} but found ${describe(t)}`, t.line, t.col);
      this.next();
      if (t.v.startsWith("__") && !this.runtimeNames) throw new CompileError(`names starting with __ are kept for the compiler's own labels`, t.line, t.col);
      return t;
    }
    isTypeStart(n = 0) {
      const t = this.peek(n);
      return (t.t === "kw" && (BASE_TYPES.has(t.v) || UNSUPPORTED_TYPES.has(t.v))) || this.isClassName(n);
    }
    isClassName(n = 0) { const t = this.peek(n); return t.t === "id" && this.classNames.has(t.v); }
    type() {
      const t = this.next();
      if (UNSUPPORTED_TYPES.has(t.v)) throw new CompileError(`'${t.v}' is not supported: SHARP-16 has 16-bit int, bool, char and string`, t.line, t.col);
      let name = t.v === "short" ? "int" : t.v;
      while (this.is("[") && this.is("]", 1)) { this.next(); this.next(); name += "[]"; }
      return { name, line: t.line, col: t.col };
    }
    skipModifiers() { const mods = new Set(); while (this.peek().t === "kw" && MODIFIERS.has(this.peek().v)) mods.add(this.next().v); return mods; }

    // ---- top level
    program() {
      const unit = { methods: [], fields: [], statements: [], classes: [] };
      while (this.peek().t !== "eof") this.topLevel(unit, false);
      return unit;
    }
    topLevel(unit, inNamespace) {
      if (this.is("using")) { while (!this.is(";") && this.peek().t !== "eof") this.next(); this.expect(";"); return; }
      if (this.is("namespace")) {
        this.next();
        this.ident("a namespace name"); while (this.accept(".")) this.ident();
        if (this.accept(";")) return;
        this.expect("{");
        while (!this.is("}")) { if (this.peek().t === "eof") this.expect("}"); this.topLevel(unit, true); }
        this.expect("}");
        return;
      }
      const save = this.p;
      this.skipModifiers();
      if (this.is("class") || this.is("struct")) {
        const kw = this.next();
        if (kw.v === "struct") throw new CompileError("structs are not supported", kw.line, kw.col);
        const name = this.ident("a class name");
        if (unit.classes.some((c) => c.name === name.v)) throw new CompileError(`class '${name.v}' is defined more than once`, name.line, name.col);
        const cls = { name: name.v, fields: [], methods: [], ctor: null, line: name.line, col: name.col };
        unit.classes.push(cls);
        this.expect("{");
        while (!this.is("}")) { if (this.peek().t === "eof") this.expect("}", "to close the class"); this.member(unit, cls); }
        this.expect("}");
        return;
      }
      if (inNamespace) { const t = this.peek(); throw new CompileError(`expected a class but found ${describe(t)}`, t.line, t.col); }
      // C# top-level statements, with local functions
      if (this.isTypeStart() && this.looksLikeMethod()) { this.method(unit); return; }
      this.p = save;
      unit.statements.push(this.statement());
    }
    looksLikeMethod() {
      let n = 1;
      while (this.is("[", n) && this.is("]", n + 1)) n += 2;
      return this.peek(n).t === "id" && this.is("(", n + 1);
    }
    member(unit, cls) {
      const mods = this.skipModifiers();
      if (this.is("class")) { const t = this.peek(); throw new CompileError("nested classes are not supported", t.line, t.col); }
      const isConst = !!this.accept("const");
      if (!isConst && this.peek().t === "id" && this.peek().v === cls.name && this.is("(", 1)) {
        const t = this.peek();
        if (mods.has("static")) throw new CompileError("static constructors are not supported", t.line, t.col);
        if (cls.ctor) throw new CompileError(`${cls.name} has more than one constructor (overloads are not supported)`, t.line, t.col);
        cls.ctor = this.method(null, { name: "void", line: t.line, col: t.col });
        return;
      }
      if (!this.isTypeStart()) { const t = this.peek(); throw new CompileError(`expected a field or a method but found ${describe(t)}`, t.line, t.col); }
      const isStatic = isConst || mods.has("static");
      if (!isConst && this.looksLikeMethod()) {
        const m = this.method(isStatic ? unit : null);
        m.owner = cls.name;
        if (!isStatic) cls.methods.push(m);
        return;
      }
      const type = this.type();
      do {
        const name = this.ident("a field name");
        let init = null;
        if (this.accept("=")) init = this.is("{") ? this.arrayInit() : this.expr();
        const field = { type, name: name.v, init, isConst, owner: cls.name, line: name.line, col: name.col };
        (isStatic ? unit.fields : cls.fields).push(field);
      } while (this.accept(","));
      this.expect(";");
    }
    method(unit, ctorType) {
      const ret = ctorType || this.type();
      const name = this.ident("a method name");
      this.expect("(");
      const params = [];
      if (!this.is(")")) {
        do {
          if (this.is("ref") || this.is("out")) { const t = this.peek(); throw new CompileError("ref and out parameters are not supported", t.line, t.col); }
          if (!this.isTypeStart()) { const t = this.peek(); throw new CompileError(`expected a parameter type but found ${describe(t)}`, t.line, t.col); }
          const type = this.type();
          const pn = this.ident("a parameter name");
          params.push({ type, name: pn.v, line: pn.line, col: pn.col });
        } while (this.accept(","));
      }
      this.expect(")");
      let body;
      if (this.accept("=>")) { const e = this.expr(); this.expect(";"); body = { k: "block", body: [ret.name === "void" ? { k: "expr", e, line: e.line, col: e.col } : { k: "return", e, line: e.line, col: e.col }], line: e.line }; }
      else body = this.block();
      const m = { name: name.v, ret, params, body, line: name.line, col: name.col };
      if (unit) unit.methods.push(m);
      return m;
    }

    // ---- statements
    block() {
      const open = this.expect("{");
      const body = [];
      while (!this.is("}")) { if (this.peek().t === "eof") this.expect("}", "to close the block"); body.push(this.statement()); }
      const close = this.expect("}");
      return { k: "block", body, line: open.line, col: open.col, endLine: close.line };
    }
    statement() {
      const t = this.peek(), at = { line: t.line, col: t.col };
      if (this.is("{")) return this.block();
      if (this.accept(";")) return { k: "empty", ...at };
      if (this.is("const") || (this.isTypeStart() && !this.is("(", 1) && !this.is(".", 1))) { const d = this.declaration(); this.expect(";"); return d; }
      if (t.t === "kw") {
        switch (t.v) {
          case "if": {
            this.next(); this.expect("("); const c = this.expr(); this.expect(")");
            const body = this.statement();
            const els = this.accept("else") ? this.statement() : null;
            return { k: "if", c, body, els, ...at };
          }
          case "while": { this.next(); this.expect("("); const c = this.expr(); this.expect(")"); return { k: "while", c, body: this.statement(), ...at }; }
          case "do": {
            this.next(); const body = this.statement(); this.expect("while"); this.expect("("); const c = this.expr(); this.expect(")"); this.expect(";");
            return { k: "do", body, c, ...at };
          }
          case "for": {
            this.next(); this.expect("(");
            let init = null;
            if (!this.is(";")) {
              if (this.isTypeStart()) init = this.declaration();
              else { const es = [this.expr()]; while (this.accept(",")) es.push(this.expr()); init = { k: "exprs", es, ...at }; }
            }
            this.expect(";");
            const c = this.is(";") ? null : this.expr();
            this.expect(";");
            const update = [];
            if (!this.is(")")) { update.push(this.expr()); while (this.accept(",")) update.push(this.expr()); }
            this.expect(")");
            return { k: "for", init, c, update, body: this.statement(), ...at };
          }
          case "foreach": {
            this.next(); this.expect("(");
            if (!this.isTypeStart()) this.expect("var");
            const type = this.type(); const name = this.ident("a loop variable"); this.expect("in");
            const coll = this.expr(); this.expect(")");
            return { k: "foreach", type, name: name.v, coll, body: this.statement(), ...at };
          }
          case "switch": return this.switchStatement();
          case "break": this.next(); this.expect(";"); return { k: "break", ...at };
          case "continue": this.next(); this.expect(";"); return { k: "continue", ...at };
          case "return": { this.next(); const e = this.is(";") ? null : this.expr(); this.expect(";"); return { k: "return", e, ...at }; }
          case "else": throw new CompileError("'else' without an 'if'", t.line, t.col);
        }
      }
      if (this.isTypeStart() && this.looksLikeMethod()) throw new CompileError("local functions are only supported at the top level of a file", t.line, t.col);
      const e = this.expr();
      if (!["assign", "call", "pre", "post", "newobj"].includes(e.k)) throw new CompileError("only assignment, call, increment, decrement and new expressions can be used as a statement", e.line, e.col);
      this.expect(";", "after the statement");
      return { k: "expr", e, ...at };
    }
    declaration() {
      const t = this.peek();
      const isConst = !!this.accept("const");
      const type = this.type();
      if (type.name === "void") throw new CompileError("a variable can not be void", type.line, type.col);
      const decls = [];
      do {
        const name = this.ident("a variable name");
        let init = null;
        if (this.accept("=")) init = this.is("{") ? this.arrayInit() : this.expr();
        else if (type.name === "var") throw new CompileError("'var' needs an initial value", name.line, name.col);
        else if (isConst) throw new CompileError("a const needs a value", name.line, name.col);
        decls.push({ name: name.v, init, line: name.line, col: name.col });
      } while (this.accept(","));
      return { k: "var", type, decls, isConst, line: t.line, col: t.col };
    }
    arrayInit() {
      const open = this.expect("{");
      const items = [];
      if (!this.is("}")) { do { if (this.is("}")) break; items.push(this.expr()); } while (this.accept(",")); }
      this.expect("}");
      return { k: "arrinit", items, line: open.line, col: open.col };
    }
    switchStatement() {
      const t = this.next();
      this.expect("("); const e = this.expr(); this.expect(")"); this.expect("{");
      const sections = [];
      while (!this.is("}")) {
        const labels = [];
        while (this.is("case") || this.is("default")) {
          const lt = this.next();
          if (lt.v === "default") labels.push({ isDefault: true, line: lt.line, col: lt.col });
          else labels.push({ e: this.expr(), line: lt.line, col: lt.col });
          this.expect(":");
        }
        if (!labels.length) { const b = this.peek(); throw new CompileError(`expected 'case' or 'default' but found ${describe(b)}`, b.line, b.col); }
        const body = [];
        while (!this.is("case") && !this.is("default") && !this.is("}")) { if (this.peek().t === "eof") this.expect("}"); body.push(this.statement()); }
        sections.push({ labels, body });
      }
      this.expect("}");
      return { k: "switch", e, sections, line: t.line, col: t.col };
    }

    // ---- expressions
    expr() { return this.assignment(); }
    assignment() {
      const left = this.conditional();
      const t = this.peek();
      if (t.t === "op" && ASSIGN_OPS.has(t.v)) {
        this.next();
        if (!["name", "index", "member"].includes(left.k)) throw new CompileError("the left side of an assignment must be a variable or an array element", left.line, left.col);
        const right = this.is("{") ? this.arrayInit() : this.assignment();
        return { k: "assign", op: t.v, t: left, e: right, line: t.line, col: t.col };
      }
      return left;
    }
    conditional() {
      const c = this.binary(0);
      if (this.is("?")) {
        const q = this.next();
        const a = this.assignment(); this.expect(":"); const b = this.assignment();
        return { k: "cond", c, a, b, line: q.line, col: q.col };
      }
      return c;
    }
    binary(level) {
      if (level >= BINARY_LEVELS.length) return this.unary();
      let left = this.binary(level + 1);
      for (;;) {
        const t = this.peek();
        if (t.t !== "op" || !BINARY_LEVELS[level].includes(t.v)) return left;
        this.next();
        const right = this.binary(level + 1);
        left = { k: "bin", op: t.v, l: left, r: right, line: t.line, col: t.col };
      }
    }
    unary() {
      const t = this.peek();
      if (t.t === "op" && ["-", "+", "!", "~"].includes(t.v)) { this.next(); return { k: "unary", op: t.v, e: this.unary(), line: t.line, col: t.col }; }
      if (t.t === "op" && (t.v === "++" || t.v === "--")) { this.next(); const e = this.unary(); return { k: "pre", op: t.v, e, line: t.line, col: t.col }; }
      if (this.is("(") && this.peek(1).t === "kw" && (BASE_TYPES.has(this.peek(1).v) || UNSUPPORTED_TYPES.has(this.peek(1).v))) {
        // a cast: (int)x, (char)(x + 1)
        const save = this.p;
        this.next();
        const type = this.type();
        if (this.accept(")")) return { k: "cast", type, e: this.unary(), line: t.line, col: t.col };
        this.p = save;
      }
      return this.postfix();
    }
    postfix() {
      let e = this.primary();
      for (;;) {
        const t = this.peek();
        if (this.accept("(")) {
          const args = [];
          if (!this.is(")")) { do { args.push(this.expr()); } while (this.accept(",")); }
          this.expect(")", "to close the argument list");
          e = { k: "call", callee: e, args, line: e.line, col: e.col };
        } else if (this.accept("[")) {
          const idx = this.expr(); this.expect("]");
          e = { k: "index", obj: e, idx, line: t.line, col: t.col };
        } else if (this.accept(".")) {
          const name = this.peek();
          if (name.t !== "id" && name.t !== "kw") throw new CompileError(`expected a member name but found ${describe(name)}`, name.line, name.col);
          this.next();
          e = { k: "member", obj: e, name: name.v, line: name.line, col: name.col };
        } else if (t.t === "op" && (t.v === "++" || t.v === "--")) {
          this.next(); e = { k: "post", op: t.v, e, line: t.line, col: t.col };
        } else return e;
      }
    }
    primary() {
      const t = this.next();
      const at = { line: t.line, col: t.col };
      switch (t.t) {
        case "num": return { k: "num", v: t.v, ...at };
        case "char": return { k: "char", v: t.v, ...at };
        case "str": return { k: "str", v: t.v, ...at };
        case "interp": return { k: "interp", parts: t.parts.map((p) => (typeof p === "string" ? p : parseExpression(p.src, p.line, p.col, this.classNames))), ...at };
        case "id": return { k: "name", name: t.v, ...at };
        case "kw":
          if (t.v === "true" || t.v === "false") return { k: "bool", v: t.v === "true" ? 1 : 0, ...at };
          if (t.v === "null") return { k: "null", ...at };
          if (t.v === "this") return { k: "this", ...at };
          if (t.v === "new") {
            if (!this.isTypeStart()) { const n = this.peek(); throw new CompileError(`expected a type after new but found ${describe(n)}`, n.line, n.col); }
            const type = this.type();
            if (this.classNames.has(type.name) && this.accept("(")) {
              const args = [];
              if (!this.is(")")) { do { args.push(this.expr()); } while (this.accept(",")); }
              this.expect(")", "to close the argument list");
              return { k: "newobj", cls: type.name, args, ...at };
            }
            if (this.accept("[")) {
              if (this.accept("]")) { const init = this.arrayInit(); return { k: "newarr", elem: type.name, size: null, init, ...at }; }
              const size = this.expr(); this.expect("]");
              const init = this.is("{") ? this.arrayInit() : null;
              return { k: "newarr", elem: type.name, size, init, ...at };
            }
            if (type.name.endsWith("[]") && this.is("{")) return { k: "newarr", elem: type.name.slice(0, -2), size: null, init: this.arrayInit(), ...at };
            throw new CompileError(this.classNames.has(type.name) ? `expected '(' after new ${type.name}` : `only arrays and objects of your own classes can be created with new`, t.line, t.col);
          }
          if (BASE_TYPES.has(t.v) && this.is(".")) return { k: "name", name: t.v, ...at }; // int.MaxValue
          break;
        case "op":
          if (t.v === "(") { const e = this.expr(); this.expect(")"); return e; }
          break;
      }
      throw new CompileError(`expected an expression but found ${describe(t)}`, t.line, t.col);
    }
  }
  function describe(t) {
    if (t.t === "eof") return "the end of the file";
    if (t.t === "str") return "a string";
    if (t.t === "num") return `'${t.raw}'`;
    if (t.t === "char") return "a character";
    return `'${t.v}'`;
  }
  function parseExpression(src, line, col, classNames) {
    const p = new Parser(lex(src, line, col));
    p.classNames = classNames;
    const e = p.expr();
    if (p.peek().t !== "eof") { const t = p.peek(); throw new CompileError(`unexpected ${describe(t)} in {…}`, t.line, t.col); }
    return e;
  }

  // ================================================================ runtime library
  const RUNTIME = {
    __print_str: { deps: [], code: `
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
        RET    2` },
    __print_int: { deps: ["__pu", "__print_str"], data: ["__s_min"], code: `
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
        JMP    __print_str` },
    __pu: { deps: [], code: `
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
        RET    2` },
    __print_bool: { deps: ["__print_str"], data: ["__s_true", "__s_false"], code: `
; Prints A as True or False
__print_bool:
        TST
        JEQ    __pb_false
        LEA    __s_true
        JMP    __print_str
__pb_false:
        LEA    __s_false
        JMP    __print_str` },
    __strlen: { deps: [], code: `
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
        RET    2` },
    __sar: { deps: [], code: `
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
        RET    2` },
  };
  const RUNTIME_DATA = {
    __s_min: `__s_min:  .STRING "32768"`,
    __s_true: `__s_true: .STRING "True"`,
    __s_false: `__s_false: .STRING "False"`,
  };

  const HEAP_RUNTIME = `
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
`;

  // ================================================================ code generation
  const JUMP_FOR = { "==": "JEQ", "!=": "JNE", "<": "JLT", ">=": "JGE", ">": "JGT", "<=": "JLE" };
  const INVERSE = { "==": "!=", "!=": "==", "<": ">=", ">=": "<", ">": "<=", "<=": ">" };
  const ARITH = { "+": "ADD", "-": "SUB", "*": "MUL", "/": "DIV", "%": "MOD", "&": "AND", "|": "OR", "^": "XOR", "<<": "SHL", ">>>": "SHR" };
  const COMMUTATIVE = new Set(["+", "*", "&", "|", "^"]);
  const KEYS = { Up: 1, Down: 2, Left: 4, Right: 8, Space: 16 };
  const INTRINSICS = { __peek: "int", __poke: "void", __fp: "int", __heapStart: "int", __roots: "int" };
  const ARRAY_OF_VALUES = 1, ARRAY_OF_REFERENCES = 2;
  const isInt = (t) => t === "int" || t === "char";
  const an = (t) => (/^[aeiou]/.test(t) ? "an " : "a ") + t;
  const isArray = (t) => typeof t === "string" && t.endsWith("[]");

  function asmChar(v) {
    if (v >= 32 && v < 127 && v !== 39 && v !== 92) return `'${String.fromCharCode(v)}'`;
    return String(v);
  }
  function asmString(s) {
    // .STRING operands: quoted runs of printable text and numbers for the rest
    const parts = []; let run = "";
    for (const ch of s) {
      const v = ch.charCodeAt(0);
      if (v > 255) throw new CompileError(`'${ch}' is not a Latin-1 character, the LCD can not show it`, 0, 0);
      if (v >= 32 && v < 127 && ch !== '"' && ch !== "\\") run += ch;
      else if (v === 10) run += "\\n";
      else if (v === 9) run += "\\t";
      else if (ch === '"') run += '\\"';
      else if (ch === "\\") run += "\\\\";
      else { if (run) parts.push(`"${run}"`); run = ""; parts.push(String(v)); }
    }
    if (run || !parts.length) parts.push(`"${run}"`);
    return parts.join(", ");
  }

  class Generator {
    constructor(unit, sourceLines) {
      this.unit = unit;
      this.src = sourceLines;
      this.out = [];
      this.labelCount = 0;
      this.runtime = new Set();
      this.strings = new Map();
      this.functions = new Map();
      this.globals = new Map();
      this.classes = new Map();
      this.instantiated = new Set();
      this.needsHeap = false;
      this.curLine = 0;
      this.lastCommentLine = 0;
    }

    // ---- output helpers
    emit(mnemonic, operand) {
      const text = operand === undefined ? `        ${mnemonic}` : `        ${mnemonic.padEnd(6)} ${operand}`;
      this.out.push({ text, line: this.curLine, kind: "ins", mnemonic, operand });
    }
    label(name) { this.out.push({ text: `${name}:`, line: this.curLine, kind: "label", name }); }
    comment(text, line = this.curLine) { this.out.push({ text: `; ${text}`, line, kind: "comment" }); }
    blank() { this.out.push({ text: "", line: 0, kind: "blank" }); }
    newLabel(hint) { return `__${hint}${++this.labelCount}`; }
    at(node) {
      if (!node || !node.line || node.line === this.curLine) return;
      this.curLine = node.line;
      if (node.line !== this.lastCommentLine && this.src[node.line - 1] !== undefined) {
        const text = this.src[node.line - 1].trim();
        if (text && text !== "{" && text !== "}") { this.comment(`${node.line}: ${text}`, node.line); this.lastCommentLine = node.line; }
      }
    }
    stringLabel(s) {
      if (!this.strings.has(s)) this.strings.set(s, `__str${this.strings.size}`);
      return this.strings.get(s);
    }
    use(name) {
      if (this.runtime.has(name)) return;
      this.runtime.add(name);
      for (const d of RUNTIME[name].deps) this.use(d);
    }

    // ---- symbols
    declareTopLevel() {
      for (const f of this.unit.fields) {
        if (this.globals.has(f.name)) fail(`'${f.name}' is defined more than once`, f);
        if (f.isConst) {
          const v = this.constValue(f.init);
          if (v === null) fail(`the value of const '${f.name}' must be a constant`, f.init || f);
          this.globals.set(f.name, { kind: "const", value: v, type: f.type.name === "var" ? "int" : f.type.name });
        } else {
          if (f.type.name === "var") fail("'var' can not be used for a field", f);
          this.globals.set(f.name, { kind: "global", label: f.name, type: f.type.name, field: f });
        }
      }
      for (const m of this.unit.methods) {
        if (this.functions.has(m.name) || this.globals.has(m.name)) fail(`'${m.name}' is defined more than once (overloads are not supported)`, m);
        if (m.ret.name === "var") fail("a method can not return var", m.ret);
        this.functions.set(m.name, m);
      }
      const classes = this.unit.classes || [];
      for (const c of classes) this.classes.set(c.name, null);
      for (const c of classes) this.declareClass(c);
      if (this.unit.statements.length) {
        if (this.functions.has("Main")) fail("a program with top-level statements can not also have a Main method", this.functions.get("Main"));
        const first = this.unit.statements[0];
        this.functions.set("Main", { name: "Main", ret: { name: "void" }, params: [], body: { k: "block", body: this.unit.statements, line: first.line }, line: first.line, col: first.col, topLevel: true });
      }
      if (!this.functions.has("Main")) fail("there is no Main method and no top-level statements: nothing to run", { line: 1, col: 1 });
    }
    declareClass(c) {
      const info = { name: c.name, fields: new Map(), methods: new Map(), def: c, refOffsets: [] };
      c.fields.forEach((f, i) => {
        if (info.fields.has(f.name)) fail(`'${f.name}' is defined more than once in ${c.name}`, f);
        if (f.type.name === "var") fail("'var' can not be used for a field", f);
        if (f.type.name === "void") fail("a field can not be void", f);
        info.fields.set(f.name, { kind: "field", off: i, type: f.type.name, cls: c.name, name: f.name, field: f });
        if (this.isRef(f.type.name)) info.refOffsets.push(i);
      });
      for (const m of c.methods) {
        if (info.methods.has(m.name) || info.fields.has(m.name)) fail(`'${m.name}' is defined more than once in ${c.name} (overloads are not supported)`, m);
        if (m.ret.name === "var") fail("a method can not return var", m.ret);
        m.cls = c.name;
        m.label = `__m_${c.name}_${m.name}`;
        info.methods.set(m.name, m);
      }
      const initialised = c.fields.filter((f) => f.init);
      if (c.ctor || initialised.length) {
        const line = c.ctor ? c.ctor.line : c.line;
        info.ctor = c.ctor || { name: c.name, ret: { name: "void" }, params: [], body: { k: "block", body: [], line }, line, col: c.col, synthetic: true };
        Object.assign(info.ctor, { cls: c.name, isCtor: true, label: `__c_${c.name}`, name: c.name, initialised });
      }
      info.size = Math.max(1, c.fields.length);
      this.classes.set(c.name, info);
    }
    isRef(t) { return isArray(t) || this.classes.has(t); }
    isClassName(name) {
      if (!this.classes.has(name)) return false;
      for (let s = this.scope; s; s = s.parent) if (s.vars.has(name)) return false;
      return !this.instanceField(name) && !this.globals.has(name);
    }
    instanceField(name) {
      const cls = this.fn && this.fn.cls && this.classes.get(this.fn.cls);
      return cls ? cls.fields.get(name) || null : null;
    }
    fieldNode(e) { return { k: "member", obj: { k: "this", line: e.line, col: e.col }, name: e.name, line: e.line, col: e.col }; }
    memberTarget(obj) {
      if (obj.k === "name") {
        if (this.isClassName(obj.name)) return { stat: this.classes.get(obj.name) };
        if (!this.isVariable(obj.name)) return null;
      }
      const t = this.typeOf(obj);
      if (this.classes.has(t)) return { inst: this.classes.get(t) };
      return null;
    }
    staticMember(info, name, node) {
      const g = this.globals.get(name);
      if (g && g.field && g.field.owner === info.name) return g;
      const f = this.functions.get(name);
      if (f && f.owner === info.name) return f;
      if (info.fields.has(name) || info.methods.has(name)) fail(`'${info.name}.${name}' needs an object: it is not static`, node);
      fail(`${info.name} has no static member '${name}'`, node);
    }
    instanceMember(info, name, node) {
      const m = info.fields.get(name) || info.methods.get(name);
      if (m) return m;
      fail(`${info.name} has no field or method '${name}'`, node);
    }
    lookup(name, node) {
      for (let s = this.scope; s; s = s.parent) if (s.vars.has(name)) return s.vars.get(name);
      const field = this.instanceField(name);
      if (field) return field;
      if (this.globals.has(name)) {
        const g = this.globals.get(name);
        return g;
      }
      if (this.functions.has(name)) fail(`'${name}' is a method: call it with ${name}(…)`, node);
      const owner = this.fn && this.fn.owner && this.classes.get(this.fn.owner);
      if (owner && owner.fields.has(name)) fail(`'${name}' is a field of each ${owner.name} object, and a static method has no object to read it from`, node);
      fail(`'${name}' is not defined`, node);
    }
    declareLocal(name, type, node) {
      for (let s = this.scope; s; s = s.parent) if (s.vars.has(name)) fail(`'${name}' is already defined in this method`, node);
      if (this.functions.has(name) && this.fn.topLevel) { /* shadowing a local function: fine in C# only in nested scopes */ }
      const slot = this.newSlot();
      const v = { kind: "local", off: -(1 + slot), type, name };
      this.scope.vars.set(name, v);
      return v;
    }
    newSlot() { return this.fn.slots++; }
    pushScope() { this.scope = { vars: new Map(), parent: this.scope }; }
    popScope() { this.scope = this.scope.parent; }

    // ---- constants
    constValue(e) {
      if (!e) return null;
      switch (e.k) {
        case "num": case "char": case "bool": return s16(e.v);
        case "name": {
          for (let s = this.scope; s; s = s.parent) if (s.vars.has(e.name)) { const v = s.vars.get(e.name); return v.kind === "const" ? v.value : null; }
          const g = this.globals.get(e.name);
          return g && g.kind === "const" ? g.value : null;
        }
        case "member": {
          if (e.obj.k === "name" && e.obj.name === "Keys" && e.name in KEYS && !this.isVariable("Keys")) return KEYS[e.name];
          if (e.obj.k === "name" && (e.obj.name === "int" || e.obj.name === "short") && e.name === "MaxValue") return 32767;
          if (e.obj.k === "name" && (e.obj.name === "int" || e.obj.name === "short") && e.name === "MinValue") return -32768;
          if (e.obj.k === "name" && this.isClassName(e.obj.name)) {
            const g = this.globals.get(e.name);
            return g && g.kind === "const" && g.field && g.field.owner === e.obj.name ? g.value : null;
          }
          return null;
        }
        case "cast": return e.type.name === "int" || e.type.name === "char" ? this.constValue(e.e) : null;
        case "unary": {
          const v = this.constValue(e.e);
          if (v === null) return null;
          if (e.op === "-") return s16(-v);
          if (e.op === "+") return v;
          if (e.op === "~") return s16(~v);
          if (e.op === "!") return v ? 0 : 1;
          return null;
        }
        case "bin": {
          const a = this.constValue(e.l), b = this.constValue(e.r);
          if (a === null || b === null) return null;
          switch (e.op) {
            case "+": return s16(a + b);
            case "-": return s16(a - b);
            case "*": return Math.max(-32768, Math.min(32767, a * b)); // as the hardware multiplier does
            case "/": if (b === 0) fail("division by the constant 0", e.r); return Math.max(-32768, Math.min(32767, Math.trunc(a / b)));
            case "%": if (b === 0) fail("division by the constant 0", e.r); return s16(a % b);
            case "&": return s16(a & b);
            case "|": return s16(a | b);
            case "^": return s16(a ^ b);
            case "<<": return s16(a << (b & 15));
            case ">>": return s16(a >> (b & 15));
            case ">>>": return s16((a & 0xffff) >>> (b & 15));
            case "==": return a === b ? 1 : 0;
            case "!=": return a !== b ? 1 : 0;
            case "<": return a < b ? 1 : 0;
            case ">": return a > b ? 1 : 0;
            case "<=": return a <= b ? 1 : 0;
            case ">=": return a >= b ? 1 : 0;
            case "&&": return a && b ? 1 : 0;
            case "||": return a || b ? 1 : 0;
          }
          return null;
        }
        case "cond": {
          const c = this.constValue(e.c);
          if (c === null) return null;
          return this.constValue(c ? e.a : e.b);
        }
      }
      return null;
    }
    isVariable(name) {
      for (let s = this.scope; s; s = s.parent) if (s.vars.has(name)) return true;
      return !!this.instanceField(name) || this.globals.has(name);
    }

    // ---- types (no code)
    typeOf(e) {
      switch (e.k) {
        case "num": return "int";
        case "char": return "char";
        case "bool": return "bool";
        case "str": case "interp": return "string";
        case "null": return "null";
        case "this": return this.thisType(e);
        case "newobj": return e.cls;
        case "name": {
          const v = this.lookup(e.name, e);
          return v.type;
        }
        case "member": {
          if (this.constValue(e) !== null) return this.memberConstType(e);
          const target = this.memberTarget(e.obj);
          if (target && target.stat) { const g = this.staticMember(target.stat, e.name, e); if (g.kind !== "global") fail(`'${e.name}' is a method: call it with ${e.name}(…)`, e); return g.type; }
          if (target && target.inst) { const f = this.instanceMember(target.inst, e.name, e); if (f.kind !== "field") fail(`'${e.name}' is a method: call it with ${e.name}(…)`, e); return f.type; }
          if (e.name === "Length") { const t = this.typeOf(e.obj); if (isArray(t) || t === "string") return "int"; }
          fail(`'${e.name}' is not supported here`, e);
          break;
        }
        case "index": {
          const t = this.typeOf(e.obj);
          if (t === "string") return "char";
          if (isArray(t)) return t.slice(0, -2);
          fail(`only arrays and strings can be indexed, not ${t}`, e);
          break;
        }
        case "cast": return e.type.name;
        case "unary": return e.op === "!" ? "bool" : "int";
        case "pre": case "post": return this.typeOf(e.e) === "char" ? "char" : "int";
        case "bin": {
          if (e.op in JUMP_FOR || e.op === "&&" || e.op === "||") return "bool";
          const l = this.typeOf(e.l), r = this.typeOf(e.r);
          if (e.op === "+" && (l === "string" || r === "string")) return "string";
          if ((e.op === "&" || e.op === "|" || e.op === "^") && l === "bool" && r === "bool") return "bool";
          return "int";
        }
        case "assign": return this.typeOf(e.t);
        case "cond": { const a = this.typeOf(e.a); return a === "null" ? this.typeOf(e.b) : a; }
        case "call": return this.callType(e);
        case "newarr": return e.elem + "[]";
        case "arrinit": return "array";
      }
      return "int";
    }
    thisType(e) {
      if (!this.fn || !this.fn.cls) fail("'this' can only be used in a constructor or a method that is not static", e);
      return this.fn.cls;
    }
    memberConstType(e) {
      if (e.obj.k === "name" && this.isClassName(e.obj.name)) return this.globals.get(e.name).type;
      return "int";
    }
    callType(e) {
      const c = e.callee;
      if (c.k === "name") {
        if (c.name in INTRINSICS) return INTRINSICS[c.name];
        const own = this.fn && this.fn.cls && this.classes.get(this.fn.cls);
        if (own && own.methods.has(c.name)) return own.methods.get(c.name).ret.name;
        const f = this.functions.get(c.name);
        if (!f) fail(`there is no method '${c.name}'`, c);
        return f.ret.name;
      }
      if (c.k === "member") {
        const target = this.memberTarget(c.obj);
        if (target && target.stat) { const f = this.staticMember(target.stat, c.name, c); if (!f.ret) fail(`'${c.name}' is a field, not a method`, c); return f.ret.name; }
        if (target && target.inst) { const m = this.instanceMember(target.inst, c.name, c); if (!m.ret) fail(`'${c.name}' is a field, not a method`, c); return m.ret.name; }
      }
      if (c.k === "member" && c.obj.k === "name") {
        const key = `${c.obj.name}.${c.name}`;
        if (key === "Keys.Read" || key === "Screen.Rgb" || key.startsWith("Math.")) return "int";
        return "void";
      }
      return "void";
    }

    // ---- "simple" expressions are loaded into A without touching B or the stack
    isSimple(e) {
      if (this.constValue(e) !== null) return true;
      if (e.k === "str") return true;
      if (e.k === "name") { const v = this.lookup(e.name, e); return v.kind === "local" || v.kind === "global" || v.kind === "const"; }
      if (e.k === "this") return true;
      if (e.k === "cast") return this.isSimple(e.e);
      return false;
    }

    // ---- program
    generate() {
      this.declareTopLevel();
      const main = this.functions.get("Main");
      this.comment("SHARP-16 assembly, compiled from C# by SharpC", 0);
      this.blank();
      const needsInit = this.unit.fields.some((f) => !f.isConst && f.init && this.globalInitValue(f) === null);
      if (needsInit) this.emit("CALL", "__init");
      for (let i = 0; i < main.params.length; i++) { this.emit("LDI", "0"); this.emit("PUSH"); }
      this.emit("CALL", "Main");
      this.emit("HLT");
      const descriptorsAt = this.out.length;
      if (needsInit) this.genInit();
      for (const f of this.functions.values()) this.genFunction(f);
      for (const info of this.classes.values()) {
        if (info.ctor) this.genFunction(info.ctor);
        for (const m of info.methods.values()) this.genFunction(m);
      }
      if (this.needsHeap) this.genHeapRuntime();
      this.out.splice(descriptorsAt, 0, ...this.descriptors());
      this.curLine = 0;
      const included = [...this.runtime];
      const data = new Set();
      for (const name of included) {
        this.blank();
        for (const line of RUNTIME[name].code.trim().split("\n")) this.out.push({ text: line, line: 0, kind: line.startsWith(";") ? "comment" : "runtime" });
        for (const d of RUNTIME[name].data || []) data.add(d);
      }
      const globals = [...this.globals.values()].filter((g) => g.kind === "global");
      globals.sort((a, b) => (a.field.runtime ? 1 : 0) - (b.field.runtime ? 1 : 0));
      if (globals.length || this.strings.size || data.size) { this.blank(); this.comment("Data", 0); }
      for (const g of globals) {
        const v = this.globalInitValue(g.field);
        const line = g.field.runtime ? 0 : g.field.line;
        this.curLine = line;
        this.out.push({ text: `${g.label}:`.padEnd(10) + `.DATA   ${v === null ? 0 : v}`, line, kind: "data" });
      }
      this.curLine = 0;
      for (const [s, label] of this.strings) this.out.push({ text: `${label}:`.padEnd(10) + `.STRING ${asmString(s)}`, line: 0, kind: "data" });
      for (const d of data) this.out.push({ text: RUNTIME_DATA[d], line: 0, kind: "data" });
      if (this.needsHeap) {
        const roots = globals.filter((g) => this.isRef(g.type) && !g.field.runtime).map((g) => g.label);
        this.comment("Static fields the garbage collector starts from", 0);
        this.out.push({ text: "__roots:".padEnd(10) + `.DATA   ${[...roots, 0].join(", ")}`, line: 0, kind: "data" });
        this.comment("Objects and arrays made with new are placed from here on, the stack grows down from the top", 0);
        this.out.push({ text: "__heap_start:", line: 0, kind: "label", name: "__heap_start" });
      }
      this.peephole();
      return this.out;
    }
    descriptors() {
      const lines = [];
      for (const name of this.instantiated) {
        const info = this.classes.get(name);
        lines.push({ text: `; ${name}: ${info.size} cell${info.size === 1 ? "" : "s"}, references at ${info.refOffsets.length ? info.refOffsets.map((o) => "+" + o).join(", ") : "none"}`, line: 0, kind: "comment" });
        lines.push({ text: `__d_${name}:`.padEnd(10) + `.DATA   ${[info.refOffsets.length, ...info.refOffsets].join(", ")}`, line: 0, kind: "data" });
      }
      if (lines.length) lines.unshift({ text: "", line: 0, kind: "blank" }, { text: "; Class layouts for the garbage collector: how many fields hold references, then where", line: 0, kind: "comment" });
      return lines;
    }
    genHeapRuntime() {
      const unit = new Parser(lex(HEAP_RUNTIME), true).program();
      const userSource = this.src;
      this.src = HEAP_RUNTIME.split("\n");
      const start = this.out.length;
      for (const f of unit.fields) {
        f.runtime = true;
        this.globals.set(f.name, { kind: "global", label: f.name, type: f.type.name, field: f });
      }
      for (const m of unit.methods) this.functions.set(m.name, m);
      this.blank();
      this.comment("The heap: allocation and a mark-and-sweep garbage collector, compiled from C#", 0);
      for (const m of unit.methods) this.genFunction(m);
      for (let i = start; i < this.out.length; i++) {
        const x = this.out[i];
        x.line = 0;
        if (x.kind === "comment") x.text = x.text.replace(/^; \d+: /, "; ");
      }
      this.src = userSource;
    }
    globalInitValue(f) {
      if (!f.init) return 0;
      const v = this.constValue(f.init);
      if (v !== null) return v;
      if (f.init.k === "str") return null;
      return null;
    }
    genInit() {
      const fn = { name: "__init", ret: { name: "void" }, params: [], slots: 0, topLevel: false };
      this.fn = fn; this.scope = null; this.pushScope();
      this.blank();
      this.comment("Static fields that need code to set them up", 0);
      this.label("__init");
      const enterIndex = this.out.length;
      this.emit("ENTER", "0");
      for (const f of this.unit.fields) {
        if (f.isConst || !f.init || this.globalInitValue(f) !== null) continue;
        this.at(f);
        this.genValue(f.init, f.type.name, f);
        this.emit("ST", f.name);
      }
      this.out[enterIndex].text = `        ${"ENTER".padEnd(6)} ${fn.slots}`;
      this.out[enterIndex].operand = String(fn.slots);
      this.emit("RET", "2");
      this.popScope();
    }
    genFunction(f) {
      const label = f.label || f.name;
      const fn = { name: f.name, ret: f.ret.name, slots: 0, retLabel: `__ret_${label}`, loops: [], topLevel: !!f.topLevel, retUsed: false, cls: f.cls || null, owner: f.owner || null };
      this.fn = fn; this.scope = null;
      this.pushScope();
      const params = f.cls ? [{ type: { name: f.cls }, name: "this", line: f.line, col: f.col }, ...f.params] : f.params;
      const k = params.length;
      params.forEach((p, j) => {
        if (this.scope.vars.has(p.name)) fail(`parameter '${p.name}' is listed twice`, p);
        if (p.type.name === "var" || p.type.name === "void") fail(`a parameter can not be ${p.type.name}`, p);
        this.scope.vars.set(p.name, { kind: "local", off: 2 + (k - 1 - j), type: p.type.name, name: p.name, param: true });
      });
      this.blank();
      this.curLine = f.line; this.lastCommentLine = 0;
      if (!f.topLevel) { this.comment(`${f.line}: ${this.src[f.line - 1].trim().replace(/\s*\{$/, "")}`, f.line); this.lastCommentLine = f.line; }
      else this.comment("Top-level statements", f.line);
      const frame = params.map((p, j) => `${p.name} FP+${2 + (k - 1 - j)}`).join(", ");
      this.label(label);
      const enterIndex = this.out.length;
      this.emit("ENTER", "0");
      if (f.isCtor) this.genFieldInitialisers(f.initialised);
      this.genBlockBody(f.body.body);
      this.curLine = f.body.endLine || this.curLine;
      this.label(fn.retLabel);
      if (f.isCtor) this.emit("LDL", String(2 + k - 1));
      this.emit("RET", String(2 + k));
      this.out[enterIndex].text = `        ${"ENTER".padEnd(6)} ${fn.slots}`;
      this.out[enterIndex].operand = String(fn.slots);
      if (frame || fn.localNames) this.out[enterIndex].text = this.out[enterIndex].text.padEnd(24) + `; ${[frame, fn.localNames].filter(Boolean).join(", ")}`;
      this.popScope();
    }
    genFieldInitialisers(fields) {
      for (const f of fields) {
        this.at(f);
        const target = { k: "member", obj: { k: "this", line: f.line, col: f.col }, name: f.name, line: f.line, col: f.col };
        this.genEffect({ k: "assign", op: "=", t: target, e: f.init, line: f.line, col: f.col });
      }
    }
    genBlockBody(stmts) {
      for (const s of stmts) this.genStatement(s);
    }
    noteLocal(v) {
      const label = `${v.name} FP${v.off}`;
      this.fn.localNames = this.fn.localNames ? `${this.fn.localNames}, ${label}` : label;
    }

    // ---- statements
    genStatement(s) {
      this.at(s);
      switch (s.k) {
        case "block": this.pushScope(); this.genBlockBody(s.body); this.popScope(); break;
        case "empty": break;
        case "var": this.genDeclaration(s); break;
        case "exprs": for (const e of s.es) this.genEffect(e); break;
        case "expr": this.genEffect(s.e); break;
        case "if": {
          const ct = this.typeOf(s.c);
          if (ct !== "bool") fail(`a condition must be a bool, not ${ct}`, s.c);
          const cv = this.constValue(s.c);
          if (cv !== null) { if (cv) this.genStatement(s.body); else if (s.els) this.genStatement(s.els); break; }
          const elseL = this.newLabel("else"), endL = s.els ? this.newLabel("endif") : elseL;
          this.genBranch(s.c, elseL, false);
          this.genStatement(s.body);
          if (s.els) { this.emit("JMP", endL); this.label(elseL); this.genStatement(s.els); }
          this.label(endL);
          break;
        }
        case "while": {
          const top = this.newLabel("loop"), cond = this.newLabel("test"), end = this.newLabel("done");
          this.emit("JMP", cond);
          this.label(top);
          this.loop(end, cond, () => this.genStatement(s.body));
          this.at(s); this.label(cond);
          this.genCondJump(s.c, top);
          this.label(end);
          break;
        }
        case "do": {
          const top = this.newLabel("loop"), cond = this.newLabel("test"), end = this.newLabel("done");
          this.label(top);
          this.loop(end, cond, () => this.genStatement(s.body));
          this.label(cond);
          this.curLine = s.c.line;
          this.genCondJump(s.c, top);
          this.label(end);
          break;
        }
        case "for": {
          this.pushScope();
          if (s.init) this.genStatement(s.init);
          const top = this.newLabel("loop"), next = this.newLabel("next"), cond = this.newLabel("test"), end = this.newLabel("done");
          this.at(s);
          this.emit("JMP", cond);
          this.label(top);
          this.loop(end, next, () => this.genStatement(s.body));
          this.curLine = 0; this.at(s);
          this.label(next);
          for (const u of s.update) this.genEffect(u);
          this.label(cond);
          if (s.c) this.genCondJump(s.c, top); else this.emit("JMP", top);
          this.label(end);
          this.popScope();
          break;
        }
        case "foreach": this.genForeach(s); break;
        case "switch": this.genSwitch(s); break;
        case "break": {
          const l = this.fn.loops[this.fn.loops.length - 1];
          if (!l) fail("'break' outside a loop or switch", s);
          this.emit("JMP", l.brk); break;
        }
        case "continue": {
          const l = [...this.fn.loops].reverse().find((x) => x.cont);
          if (!l) fail("'continue' outside a loop", s);
          this.emit("JMP", l.cont); break;
        }
        case "return": {
          if (s.e) {
            if (this.fn.ret === "void") fail(`${this.fn.name} is void and can not return a value`, s.e);
            this.genValue(s.e, this.fn.ret, s.e);
          } else if (this.fn.ret !== "void") fail(`${this.fn.name} must return ${an(this.fn.ret)}`, s);
          this.emit("JMP", this.fn.retLabel);
          break;
        }
        default: fail(`unsupported statement`, s);
      }
    }
    loop(brk, cont, body) { this.fn.loops.push({ brk, cont }); body(); this.fn.loops.pop(); }
    genDeclaration(s) {
      for (const d of s.decls) {
        let type = s.type.name;
        if (s.isConst) {
          const v = this.constValue(d.init);
          if (v === null) fail(`the value of const '${d.name}' must be a constant`, d.init);
          for (let sc = this.scope; sc; sc = sc.parent) if (sc.vars.has(d.name)) fail(`'${d.name}' is already defined in this method`, d);
          this.scope.vars.set(d.name, { kind: "const", value: v, type: type === "var" ? this.typeOf(d.init) : type });
          continue;
        }
        if (type === "var") {
          if (d.init.k === "arrinit") fail("'var' needs a type for an array initializer: write int[] or new[] { … }", d.init);
          type = this.typeOf(d.init);
          if (type === "null" || type === "void") fail(`can not infer a type from ${type}`, d.init);
        }
        if (d.init) { this.at(d); this.genValue(d.init, type, d); }
        const v = this.declareLocal(d.name, type, d);
        this.noteLocal(v);
        if (d.init) this.emit("STL", String(v.off));
      }
    }
    genForeach(s) {
      this.pushScope();
      const collType = this.typeOf(s.coll);
      if (!isArray(collType) && collType !== "string") fail(`foreach works on arrays and strings, not ${collType}`, s.coll);
      const elemType = collType === "string" ? "char" : collType.slice(0, -2);
      const ptr = this.declareLocal(`__each${this.labelCount}`, collType, s);
      const idx = this.declareLocal(`__index${this.labelCount}`, "int", s);
      this.genExpr(s.coll);
      this.emit("STL", String(ptr.off));
      this.emit("LDI", "0");
      this.emit("STL", String(idx.off));
      const top = this.newLabel("loop"), next = this.newLabel("next"), cond = this.newLabel("test"), end = this.newLabel("done");
      this.emit("JMP", cond);
      this.label(top);
      this.pushScope();
      const x = this.declareLocal(s.name, s.type.name === "var" ? elemType : s.type.name, s);
      this.noteLocal(x);
      this.emit("LDL", String(ptr.off)); this.emit("TAB"); this.emit("LDL", String(idx.off)); this.emit("ADD");
      if (collType !== "string") this.emit("INC");
      this.emit("LDX");
      this.emit("STL", String(x.off));
      this.loop(end, next, () => this.genStatement(s.body));
      this.popScope();
      this.at(s);
      this.label(next);
      this.emit("LDL", String(idx.off)); this.emit("INC"); this.emit("STL", String(idx.off));
      this.label(cond);
      if (collType === "string") {
        this.emit("LDL", String(ptr.off)); this.emit("TAB"); this.emit("LDL", String(idx.off)); this.emit("ADD"); this.emit("LDX");
        this.emit("TST"); this.emit("JNE", top);
      } else {
        this.emit("LDL", String(idx.off)); this.emit("TAB"); this.emit("LDL", String(ptr.off)); this.emit("LDX");
        this.emit("CMP"); this.emit("JLT", top);
      }
      this.label(end);
      this.popScope();
    }
    genSwitch(s) {
      const t = this.typeOf(s.e);
      if (!isInt(t) && t !== "bool") fail(`switch works on int, char and bool, not ${t}`, s.e);
      this.genExpr(s.e);
      this.emit("TAB");
      const end = this.newLabel("endswitch");
      let defaultLabel = null;
      const seen = new Set();
      const sectionLabels = s.sections.map((sec) => {
        const l = this.newLabel("case");
        for (const lab of sec.labels) {
          if (lab.isDefault) { defaultLabel = l; continue; }
          const v = this.constValue(lab.e);
          if (v === null) fail("a case label must be a constant", lab.e);
          if (seen.has(v)) fail(`case ${v} appears twice`, lab.e);
          seen.add(v);
          this.emit("LDI", t === "char" ? asmChar(v & 0xffff) : String(v));
          this.emit("CMP");
          this.emit("JEQ", l);
        }
        return l;
      });
      this.emit("JMP", defaultLabel || end);
      this.fn.loops.push({ brk: end, cont: null });
      s.sections.forEach((sec, i) => {
        this.label(sectionLabels[i]);
        this.pushScope();
        this.genBlockBody(sec.body);
        this.popScope();
      });
      this.fn.loops.pop();
      this.label(end);
    }

    // ---- branching on a condition
    genCondJump(c, label) { this.genBranch(c, label, true); }
    genBranch(e, label, jumpIf) {
      const t = this.typeOf(e);
      if (t !== "bool") fail(`a condition must be a bool, not ${t}`, e);
      const cv = this.constValue(e);
      if (cv !== null) { if (!!cv === jumpIf) this.emit("JMP", label); return; }
      if (e.k === "unary" && e.op === "!") return this.genBranch(e.e, label, !jumpIf);
      if (e.k === "bin" && (e.op === "&&" || e.op === "||")) {
        const and = e.op === "&&";
        if (and !== jumpIf) { this.genBranch(e.l, label, jumpIf); this.genBranch(e.r, label, jumpIf); }
        else { const skip = this.newLabel("skip"); this.genBranch(e.l, skip, !jumpIf); this.genBranch(e.r, label, jumpIf); this.label(skip); }
        return;
      }
      if (e.k === "bin" && e.op in JUMP_FOR) {
        const op = jumpIf ? e.op : INVERSE[e.op];
        const lt = this.typeOf(e.l), rt = this.typeOf(e.r);
        this.checkComparable(e, lt, rt);
        if (this.constValue(e.r) === 0 || e.r.k === "null") { this.genExpr(e.l); this.emit("TST"); }
        else { this.genOperands(e.l, e.r, false); this.emit("CMP"); }
        this.emit(JUMP_FOR[op], label);
        return;
      }
      this.genExpr(e);
      this.emit("TST");
      this.emit(jumpIf ? "JNE" : "JEQ", label);
    }
    checkComparable(e, lt, rt) {
      if (lt === "string" || rt === "string") fail("comparing strings is not supported", e);
      if (this.isRef(lt) || this.isRef(rt) || lt === "null" || rt === "null") {
        if (e.op !== "==" && e.op !== "!=") fail(`'${e.op}' does not work on ${this.isRef(lt) ? lt : rt}: references can only be compared with == and !=`, e);
        if (lt !== rt && lt !== "null" && rt !== "null") fail(`can not compare ${an(lt)} with ${an(rt)}`, e);
        return;
      }
      if ((lt === "bool") !== (rt === "bool") && !(e.op === "==" || e.op === "!=")) fail(`can not compare ${lt} and ${rt}`, e);
      if ((lt === "bool") !== (rt === "bool")) fail(`can not compare ${lt} and ${rt}`, e);
      if (lt === "bool" && !["==", "!="].includes(e.op)) fail(`'${e.op}' does not work on bool`, e);
    }

    // B = left, A = right
    genOperands(l, r, commutative) {
      if (this.isSimple(r)) { this.genExpr(l); this.emit("TAB"); this.genExpr(r); return; }
      if (commutative && this.constValue(l) !== null) { this.genExpr(r); this.emit("TAB"); this.genExpr(l); return; }
      this.genExpr(l); this.emit("PUSH"); this.genExpr(r); this.emit("POP");
    }

    // ---- expressions: genValue checks the type fits where it goes
    genValue(e, type, node) {
      if (e.k === "arrinit") {
        if (!isArray(type)) fail(`an array initializer needs an array type, not ${type}`, e);
        return this.genArray(type.slice(0, -2), null, e);
      }
      const t = this.genExpr(e);
      this.checkAssignable(type, t, node || e);
      return t;
    }
    checkAssignable(to, from, node) {
      if (to === from) return;
      if (to === "int" && from === "char") return;
      if (to === "char" && from === "int" && node && this.constValue(node.init || node) !== null) return;
      if ((this.isRef(to) || to === "string") && from === "null") return;
      if (to === "char" && from === "int") fail("can not put an int in a char without a cast: write (char)", node);
      fail(`can not use ${an(from)} where ${an(to)} is needed`, node);
    }
    genEffect(e) {
      // an expression whose value is not used
      if (e.k === "post") return this.genIncDec(e, false, true);
      if (e.k === "pre") return this.genIncDec(e, true, true);
      if (e.k === "call") { this.genCall(e); return; }
      this.genExpr(e);
    }
    genExpr(e) {
      const cv = this.constValue(e);
      if (cv !== null && e.k !== "name") {
        const t = this.typeOf(e);
        this.emit("LDI", t === "char" ? asmChar(cv & 0xffff) : String(cv));
        return t;
      }
      switch (e.k) {
        case "str": this.emit("LEA", this.stringLabel(e.v)); return "string";
        case "interp": case "null":
          if (e.k === "null") { this.emit("LDI", "0"); return "null"; }
          fail("an interpolated string can only be used directly in Console.Write or Console.WriteLine", e);
          break;
        case "this": this.thisType(e); this.emit("LDL", String(this.lookup("this", e).off)); return this.fn.cls;
        case "newobj": return this.genNewObject(e);
        case "name": {
          const v = this.lookup(e.name, e);
          if (v.kind === "field") return this.genExpr(this.fieldNode(e));
          if (v.kind === "const") this.emit("LDI", v.type === "char" ? asmChar(v.value & 0xffff) : String(v.value));
          else if (v.kind === "local") this.emit("LDL", String(v.off));
          else this.emit("LD", v.label);
          return v.type;
        }
        case "member": {
          const target = this.memberTarget(e.obj);
          if (target && target.stat) {
            const g = this.staticMember(target.stat, e.name, e);
            if (g.kind !== "global") fail(`'${e.name}' is a method: call it with ${e.name}(…)`, e);
            this.emit("LD", g.label);
            return g.type;
          }
          if (target && target.inst) {
            const t = this.genFieldAddress(e);
            this.emit("LDX");
            return t;
          }
          if (e.name === "Length") {
            const t = this.genExpr(e.obj);
            if (isArray(t)) { this.emit("LDX"); return "int"; }
            if (t === "string") { this.use("__strlen"); this.emit("CALL", "__strlen"); return "int"; }
          }
          fail(`'${e.name}' is not supported here`, e);
          break;
        }
        case "index": {
          const et = this.genElementAddress(e);
          this.emit("LDX");
          return et;
        }
        case "cast": {
          const t = this.genExpr(e.e);
          const to = e.type.name;
          if (to === t) return t;
          if ((to === "int" || to === "char") && isInt(t)) return to;
          fail(`can not cast ${t} to ${to}`, e);
          break;
        }
        case "unary": {
          const t = this.genExpr(e.e);
          if (e.op === "!") { if (t !== "bool") fail(`'!' needs a bool, not ${t}`, e); this.emit("TAB"); this.emit("LDI", "1"); this.emit("XOR"); return "bool"; }
          if (!isInt(t)) fail(`'${e.op}' needs a number, not ${t}`, e);
          if (e.op === "-") this.emit("NEG");
          if (e.op === "~") this.emit("NOT");
          return "int";
        }
        case "pre": return this.genIncDec(e, true, false);
        case "post": return this.genIncDec(e, false, false);
        case "bin": return this.genBinary(e);
        case "assign": return this.genAssign(e);
        case "cond": {
          const ct = this.typeOf(e.c);
          if (ct !== "bool") fail(`the condition of ?: must be a bool, not ${ct}`, e.c);
          const elseL = this.newLabel("else"), end = this.newLabel("endif");
          this.genBranch(e.c, elseL, false);
          const ta = this.genExpr(e.a);
          this.emit("JMP", end);
          this.label(elseL);
          const tb = this.genExpr(e.b);
          this.label(end);
          if (ta !== tb && !(isInt(ta) && isInt(tb)) && ta !== "null" && tb !== "null") fail(`the two sides of ?: are ${ta} and ${tb}`, e);
          return ta === "null" ? tb : ta === tb ? ta : "int";
        }
        case "call": {
          const t = this.genCall(e);
          if (t === "void") fail("this method returns nothing (void), so it has no value to use", e);
          return t;
        }
        case "newarr": {
          if (e.init) return this.genArray(e.elem, e.size, e.init);
          const st = this.genExpr(e.size);
          if (!isInt(st)) fail(`an array size must be an int, not ${st}`, e.size);
          this.emit("PUSH");
          this.callNewArray(e.elem);
          return e.elem + "[]";
        }
        case "arrinit": fail("an array initializer can only be used to set up an array variable", e); break;
      }
      fail("this expression is not supported", e);
    }
    genArray(elem, sizeExpr, init) {
      const n = init.items.length;
      if (sizeExpr) { const sv = this.constValue(sizeExpr); if (sv !== n) fail(`the array has ${n} element${n === 1 ? "" : "s"}, but its size says ${sv === null ? "something else" : sv}`, sizeExpr); }
      this.emit("LDI", String(n));
      this.emit("PUSH");
      this.callNewArray(elem);
      const tmp = { off: -(1 + this.newSlot()) };
      this.emit("STL", String(tmp.off));
      init.items.forEach((item, i) => {
        if (this.isSimple(item)) {
          this.emit("LDL", String(tmp.off)); this.emit("TAB"); this.emit("LDI", String(i + 1)); this.emit("ADD"); this.emit("TAB");
          this.genValue(item, elem, item);
        } else {
          this.emit("LDL", String(tmp.off)); this.emit("TAB"); this.emit("LDI", String(i + 1)); this.emit("ADD"); this.emit("PUSH");
          this.genValue(item, elem, item);
          this.emit("POP");
        }
        this.emit("STX");
      });
      this.emit("LDL", String(tmp.off));
      return elem + "[]";
    }
    callNewArray(elem) {
      this.needsHeap = true;
      this.emit("LDI", String(this.isRef(elem) ? ARRAY_OF_REFERENCES : ARRAY_OF_VALUES));
      this.emit("PUSH");
      this.emit("CALL", "__NewArray");
    }
    genNewObject(e) {
      const info = this.classes.get(e.cls);
      this.needsHeap = true;
      this.instantiated.add(e.cls);
      this.emit("LDI", String(info.size));
      this.emit("PUSH");
      this.emit("LEA", `__d_${e.cls}`);
      this.emit("PUSH");
      this.emit("CALL", "__New");
      if (info.ctor) {
        this.emit("PUSH");
        this.genArgs(info.ctor, e.args, e, `new ${e.cls}`);
        this.emit("CALL", info.ctor.label);
      } else if (e.args.length) fail(`${e.cls} has no constructor, so new ${e.cls}() takes no arguments`, e);
      return e.cls;
    }
    genFieldAddress(e) {
      const info = this.classes.get(this.typeOf(e.obj));
      const f = this.instanceMember(info, e.name, e);
      if (f.kind !== "field") fail(`'${e.name}' is a method: call it with ${e.name}(…)`, e);
      this.genExpr(e.obj);
      if (f.off === 1) this.emit("INC");
      else if (f.off) { this.emit("TAB"); this.emit("LDI", String(f.off)); this.emit("ADD"); }
      return f.type;
    }
    genAddress(e) { return e.k === "index" ? this.genElementAddress(e) : this.genFieldAddress(e); }
    lvalue(target) {
      if (target.k === "name") {
        const v = this.lookup(target.name, target);
        return v.kind === "field" ? this.fieldNode(target) : target;
      }
      if (target.k === "member") {
        const t = this.memberTarget(target.obj);
        if (t && t.stat) {
          const g = this.staticMember(t.stat, target.name, target);
          if (g.kind === "const") fail(`'${target.name}' is a constant and can not be changed`, target);
          if (g.kind !== "global") fail(`'${target.name}' is a method and can not be assigned`, target);
          return { k: "name", name: target.name, line: target.line, col: target.col };
        }
        if (!t) fail(`'${target.name}' can not be assigned`, target);
      }
      return target;
    }
    // A = the address of arr[i]; returns the element type
    genElementAddress(e) {
      const t = this.typeOf(e.obj);
      if (!isArray(t) && t !== "string") fail(`only arrays and strings can be indexed, not ${t}`, e);
      const it = this.typeOf(e.idx);
      if (!isInt(it)) fail(`an index must be an int, not ${it}`, e.idx);
      const iv = this.constValue(e.idx);
      const extra = t === "string" ? 0 : 1;
      if (iv !== null) {
        this.genExpr(e.obj);
        if (iv + extra === 1) this.emit("INC");
        else if (iv + extra !== 0) { this.emit("TAB"); this.emit("LDI", String(iv + extra)); this.emit("ADD"); }
      } else {
        this.genOperands(e.obj, e.idx, true);
        this.emit("ADD");
        if (extra) this.emit("INC");
      }
      return t === "string" ? "char" : t.slice(0, -2);
    }
    genBinary(e) {
      const op = e.op;
      if (op in JUMP_FOR || op === "&&" || op === "||") {
        // a bool value: 1 or 0
        if (op in JUMP_FOR) {
          const lt = this.typeOf(e.l), rt = this.typeOf(e.r);
          this.checkComparable(e, lt, rt);
          if (this.constValue(e.r) === 0 || e.r.k === "null") { this.genExpr(e.l); this.emit("TST"); }
          else { this.genOperands(e.l, e.r, false); this.emit("CMP"); }
          const yes = this.newLabel("true");
          this.emit("LDI", "1");
          this.emit(JUMP_FOR[op], yes);
          this.emit("LDI", "0");
          this.label(yes);
          return "bool";
        }
        const no = this.newLabel("false"), end = this.newLabel("bool");
        this.genBranch(e, no, false);
        this.emit("LDI", "1");
        this.emit("JMP", end);
        this.label(no);
        this.emit("LDI", "0");
        this.label(end);
        return "bool";
      }
      const lt = this.typeOf(e.l), rt = this.typeOf(e.r);
      if (op === "+" && (lt === "string" || rt === "string")) fail("joining strings with + only works directly inside Console.Write or Console.WriteLine", e);
      const bools = lt === "bool" && rt === "bool" && (op === "&" || op === "|" || op === "^");
      if (!bools) {
        if (!isInt(lt)) fail(`'${op}' needs numbers, but the left side is ${lt}`, e.l);
        if (!isInt(rt)) fail(`'${op}' needs numbers, but the right side is ${rt}`, e.r);
      }
      const rv = this.constValue(e.r);
      if ((op === "+" || op === "-") && (rv === 1 || rv === -1)) {
        this.genExpr(e.l);
        this.emit((op === "+") === (rv === 1) ? "INC" : "DEC");
        return "int";
      }
      if ((op === "+" || op === "-" || op === "|" || op === "^" || op === "<<" || op === ">>" || op === ">>>") && rv === 0) { this.genExpr(e.l); return "int"; }
      if (op === ">>") {
        this.genOperands(e.l, e.r, false);
        this.use("__sar");
        this.emit("CALL", "__sar");
        return "int";
      }
      this.genOperands(e.l, e.r, COMMUTATIVE.has(op));
      this.emit(ARITH[op]);
      return bools ? "bool" : "int";
    }
    // ++x, x++, --x, x--
    genIncDec(e, prefix, discard) {
      const target = this.lvalue(e.e);
      const ins = e.op === "++" ? "INC" : "DEC", undo = e.op === "++" ? "DEC" : "INC";
      const t = this.typeOf(target);
      if (!isInt(t)) fail(`'${e.op}' needs a number, not ${t}`, e);
      if (target.k === "name") {
        const v = this.lookup(target.name, target);
        if (v.kind === "const") fail(`'${target.name}' is a constant`, target);
        this.genExpr(target);
        this.emit(ins);
        this.store(v);
        if (!prefix && !discard) this.emit(undo);
        return t;
      }
      if (target.k === "index" || target.k === "member") {
        if (target.k === "index" && this.typeOf(target.obj) === "string") fail("strings can not be changed", target);
        this.genAddress(target);
        this.emit("PUSH");
        this.emit("LDX");
        this.emit(ins);
        this.emit("POP");
        this.emit("STX");
        if (!prefix && !discard) this.emit(undo);
        return t;
      }
      fail(`'${e.op}' needs a variable, a field or an array element`, e);
    }
    store(v) {
      if (v.kind === "local") this.emit("STL", String(v.off));
      else if (v.kind === "global") this.emit("ST", v.label);
      else fail("can not assign to a constant", null);
    }
    genAssign(e) {
      const target = this.lvalue(e.t);
      const tt = this.typeOf(target);
      const binop = e.op === "=" ? null : e.op.slice(0, -1);
      if (target.k === "name") {
        const v = this.lookup(target.name, target);
        if (v.kind === "const") fail(`'${target.name}' is a constant and can not be changed`, target);
        if (!binop) this.genValue(e.e, tt, e.e);
        else this.genCompound(target, binop, e.e, tt);
        this.store(v);
        return tt;
      }
      // an array element
      if (target.k === "index" && this.typeOf(target.obj) === "string") fail("strings can not be changed", target);
      this.genAddress(target);
      if (!binop) {
        if (this.isSimple(e.e)) { this.emit("TAB"); this.genValue(e.e, tt, e.e); }
        else { this.emit("PUSH"); this.genValue(e.e, tt, e.e); this.emit("POP"); }
      } else {
        this.emit("PUSH");
        this.emit("LDX");
        this.genCompoundOnA(binop, e.e, tt, e);
        this.emit("POP");
      }
      this.emit("STX");
      return tt;
    }
    genCompound(target, binop, value, tt) {
      const node = { k: "bin", op: binop, l: target, r: value, line: value.line, col: value.col };
      const t = this.genBinary(node);
      if (tt === "char" && t === "int") return; // x += 1 on a char stays a char, as in C#
      if (tt === "bool" && t !== "bool") fail(`can not use '${binop}=' on a bool`, value);
    }
    // A holds the current value; A = A op value
    genCompoundOnA(binop, value, tt, node) {
      const vt = this.typeOf(value);
      if (!isInt(vt) && !(tt === "bool" && vt === "bool")) fail(`'${binop}=' needs a number, not ${vt}`, value);
      const rv = this.constValue(value);
      if ((binop === "+" || binop === "-") && (rv === 1 || rv === -1)) { this.emit((binop === "+") === (rv === 1) ? "INC" : "DEC"); return; }
      if (this.isSimple(value)) { this.emit("TAB"); this.genExpr(value); }
      else { this.emit("PUSH"); this.genExpr(value); this.emit("POP"); }
      if (binop === ">>") { this.use("__sar"); this.emit("CALL", "__sar"); }
      else this.emit(ARITH[binop]);
    }

    // ---- calls
    genCall(e) {
      const c = e.callee;
      if (c.k === "name") {
        if (c.name in INTRINSICS) return this.genIntrinsic(c.name, e);
        const own = this.fn && this.fn.cls && this.classes.get(this.fn.cls);
        if (own && own.methods.has(c.name)) {
          const m = own.methods.get(c.name);
          this.emit("LDL", String(this.lookup("this", c).off));
          this.emit("PUSH");
          this.genArgs(m, e.args, e, c.name);
          this.emit("CALL", m.label);
          return m.ret.name;
        }
        const f = this.functions.get(c.name);
        if (!f) {
          const owner = this.fn && this.fn.owner && this.classes.get(this.fn.owner);
          if (owner && owner.methods.has(c.name)) fail(`'${c.name}' works on a ${owner.name} object, and a static method has no object to call it on`, c);
          fail(this.isVariable(c.name) ? `'${c.name}' is a variable, not a method` : `there is no method '${c.name}'`, c);
        }
        this.genArgs(f, e.args, e, c.name);
        this.emit("CALL", f.label || f.name);
        return f.ret.name;
      }
      if (c.k === "member") {
        const target = this.memberTarget(c.obj);
        if (target && target.stat) {
          const f = this.staticMember(target.stat, c.name, c);
          if (!f.ret) fail(`'${c.name}' is a field, not a method`, c);
          this.genArgs(f, e.args, e, `${target.stat.name}.${c.name}`);
          this.emit("CALL", f.name);
          return f.ret.name;
        }
        if (target && target.inst) {
          const m = this.instanceMember(target.inst, c.name, c);
          if (!m.ret) fail(`'${c.name}' is a field, not a method`, c);
          this.genExpr(c.obj);
          this.emit("PUSH");
          this.genArgs(m, e.args, e, c.name);
          this.emit("CALL", m.label);
          return m.ret.name;
        }
        if (c.obj.k === "name") return this.genBuiltin(`${c.obj.name}.${c.name}`, e);
      }
      fail("this call is not supported", e);
    }
    genArgs(f, args, e, name) {
      if (args.length !== f.params.length) fail(`${name} takes ${f.params.length} argument${f.params.length === 1 ? "" : "s"}, not ${args.length}`, e);
      args.forEach((a, i) => { this.genValue(a, f.params[i].type.name, a); this.emit("PUSH"); });
    }
    genIntrinsic(name, e) {
      switch (name) {
        case "__peek": this.args(e, 1, name); this.genInt(e.args[0]); this.emit("LDX"); break;
        case "__poke": this.args(e, 2, name); this.genOperands(e.args[0], e.args[1], false); this.emit("STX"); break;
        case "__fp": this.args(e, 0, name); this.emit("LEAL", "0"); break;
        case "__heapStart": this.args(e, 0, name); this.emit("LEA", "__heap_start"); break;
        case "__roots": this.args(e, 0, name); this.emit("LEA", "__roots"); break;
      }
      return INTRINSICS[name];
    }
    args(e, n, name) { if (e.args.length !== n) fail(`${name} takes ${n} argument${n === 1 ? "" : "s"}`, e); }
    genInt(a) { const t = this.genExpr(a); if (!isInt(t)) fail(`expected a number, not ${t}`, a); }
    genBuiltin(name, e) {
      switch (name) {
        case "Console.Write": case "Console.WriteLine": {
          if (e.args.length > 1) {
            const fmt = e.args[0];
            if (fmt.k !== "str") fail("with more than one argument, the first must be a format string such as \"{0} and {1}\"", fmt);
            this.printFormat(fmt, e.args.slice(1));
          } else if (e.args.length === 1) this.printValue(e.args[0]);
          if (name === "Console.WriteLine") { this.emit("LDI", "10"); this.emit("OUT"); }
          return "void";
        }
        case "Console.Clear": this.args(e, 0, name); this.emit("CLT"); return "void";
        case "Screen.Clear": this.args(e, 0, name); this.emit("CLS"); return "void";
        case "Screen.Plot":
          this.args(e, 3, name);
          this.genInt(e.args[0]); this.emit("PX");
          this.genInt(e.args[1]); this.emit("PY");
          this.genInt(e.args[2]); this.emit("PLOT");
          return "void";
        case "Screen.MoveTo": this.args(e, 2, name); this.genInt(e.args[0]); this.emit("PX"); this.genInt(e.args[1]); this.emit("PY"); return "void";
        case "Screen.Put": this.args(e, 1, name); this.genInt(e.args[0]); this.emit("PLOT"); return "void";
        case "Screen.Rgb": {
          this.args(e, 3, name);
          const [r, g, b] = e.args;
          const rgb = { k: "bin", op: "|", line: e.line, col: e.col,
            l: { k: "bin", op: "|", line: e.line, col: e.col,
              l: { k: "bin", op: "<<", l: { k: "bin", op: ">>>", l: r, r: { k: "num", v: 3 }, line: e.line, col: e.col }, r: { k: "num", v: 11 }, line: e.line, col: e.col },
              r: { k: "bin", op: "<<", l: { k: "bin", op: ">>>", l: g, r: { k: "num", v: 2 }, line: e.line, col: e.col }, r: { k: "num", v: 5 }, line: e.line, col: e.col } },
            r: { k: "bin", op: ">>>", l: b, r: { k: "num", v: 3 }, line: e.line, col: e.col } };
          const cv = this.constValue(rgb);
          if (cv !== null) this.emit("LDI", "0x" + (cv & 0xffff).toString(16).toUpperCase().padStart(4, "0"));
          else this.genBinary(rgb);
          return "int";
        }
        case "Keys.Read": this.args(e, 0, name); this.emit("IN"); return "int";
        case "Machine.Halt": case "Environment.Exit": this.emit("HLT"); return "void";
        case "GC.Collect": this.args(e, 0, name); this.needsHeap = true; this.emit("CALL", "__Collect"); return "void";
        case "Math.Abs": {
          this.args(e, 1, name); this.genInt(e.args[0]);
          const l = this.newLabel("abs"); this.emit("TST"); this.emit("JGE", l); this.emit("NEG"); this.label(l);
          return "int";
        }
        case "Math.Min": case "Math.Max": {
          this.args(e, 2, name);
          const [a, b] = e.args;
          if (!isInt(this.typeOf(a)) || !isInt(this.typeOf(b))) fail(`${name} needs two numbers`, e);
          this.genOperands(a, b, false);
          const l = this.newLabel(name === "Math.Min" ? "min" : "max");
          this.emit("CMP");
          this.emit(name === "Math.Min" ? "JGE" : "JLE", l);
          this.emit("TBA");
          this.label(l);
          return "int";
        }
      }
      fail(`'${name}' is not available. SHARP-16 knows Console.Write, Console.WriteLine, Console.Clear, Screen.Plot, Screen.MoveTo, Screen.Put, Screen.Clear, Screen.Rgb, Keys.Read, Math.Abs, Math.Min, Math.Max, GC.Collect and Machine.Halt`, e);
    }
    printValue(a) {
      if (a.k === "bin" && a.op === "+" && this.typeOf(a) === "string") { this.printValue(a.l); this.printValue(a.r); return; }
      if (a.k === "interp") { for (const p of a.parts) { if (typeof p === "string") this.printText(p); else this.printValue(p); } return; }
      if (a.k === "str") { this.printText(a.v); return; }
      const cv = this.constValue(a);
      const t = this.typeOf(a);
      if (cv !== null && t !== "char") { this.printText(t === "bool" ? (cv ? "True" : "False") : String(cv)); return; }
      this.genExpr(a);
      switch (t) {
        case "char": this.emit("OUT"); return;
        case "int": this.use("__print_int"); this.emit("CALL", "__print_int"); return;
        case "bool": this.use("__print_bool"); this.emit("CALL", "__print_bool"); return;
        case "string": this.use("__print_str"); this.emit("CALL", "__print_str"); return;
      }
      fail(`can not print a ${t}`, a);
    }
    printText(s) {
      if (!s.length) return;
      if (s.length <= 2) { for (const ch of s) { this.emit("LDI", asmChar(ch.charCodeAt(0))); this.emit("OUT"); } return; }
      asmString(s);
      this.emit("LEA", this.stringLabel(s));
      this.use("__print_str");
      this.emit("CALL", "__print_str");
    }
    printFormat(fmt, args) {
      const s = fmt.v;
      let text = "";
      for (let i = 0; i < s.length; i++) {
        if (s[i] === "{" && s[i + 1] === "{") { text += "{"; i++; continue; }
        if (s[i] === "}" && s[i + 1] === "}") { text += "}"; i++; continue; }
        if (s[i] === "{") {
          const close = s.indexOf("}", i);
          const index = s.slice(i + 1, close);
          if (close < 0 || !/^\d+$/.test(index)) fail("format items must look like {0}, without alignment or format strings", fmt);
          if (+index >= args.length) fail(`{${index}} has no argument`, fmt);
          this.printText(text); text = "";
          this.printValue(args[+index]);
          i = close; continue;
        }
        text += s[i];
      }
      this.printText(text);
    }

    // ---- small clean ups on the finished listing
    peephole() {
      const out = this.out;
      const isIns = (x) => x && (x.kind === "ins");
      let changed = true;
      while (changed) {
        changed = false;
        for (let i = 0; i < out.length; i++) {
          const a = out[i];
          if (!isIns(a)) continue;
          // JMP to the very next label
          if (a.mnemonic === "JMP") {
            let j = i + 1;
            while (j < out.length && (out[j].kind === "label" || out[j].kind === "comment" || out[j].kind === "blank")) {
              if (out[j].kind === "label" && out[j].name === a.operand) { out.splice(i, 1); changed = true; break; }
              j++;
            }
            if (changed) break;
          }
          // find the next instruction, stopping at labels
          let j = i + 1;
          while (j < out.length && (out[j].kind === "comment" || out[j].kind === "blank")) j++;
          const b = out[j];
          if (!isIns(b)) continue;
          if ((a.mnemonic === "STL" && b.mnemonic === "LDL" && a.operand === b.operand) || (a.mnemonic === "ST" && b.mnemonic === "LD" && a.operand === b.operand)) { out.splice(j, 1); changed = true; break; }
          if (a.mnemonic === "PUSH" && b.mnemonic === "POP") { out.splice(j, 1); a.mnemonic = "TAB"; a.text = "        TAB"; changed = true; break; }
          if (a.mnemonic === "TST" && b.mnemonic === "TST") { out.splice(j, 1); changed = true; break; }
        }
      }
      // labels nobody jumps to (other than functions) are dropped
      const used = new Set();
      for (const x of out) if (x.kind === "ins" && x.operand) used.add(x.operand);
      for (const x of out) if (x.kind === "runtime" || x.kind === "data") for (const m of x.text.matchAll(/__[A-Za-z0-9_]+/g)) used.add(m[0]);
      for (let i = out.length - 1; i >= 0; i--) {
        const x = out[i];
        if (x.kind === "label" && x.name.startsWith("__") && !x.name.startsWith("__ret_") && !used.has(x.name) && x.name !== "__init" && x.name !== "__heap_start") out.splice(i, 1);
        else if (x.kind === "label" && x.name.startsWith("__ret_") && !used.has(x.name)) out.splice(i, 1);
      }
    }
  }

  // ================================================================ entry point
  function compile(source) {
    const sourceLines = source.split(/\r\n|\r|\n/);
    try {
      const unit = new Parser(lex(source)).program();
      const gen = new Generator(unit, sourceLines);
      const lines = gen.generate();
      // fold labels into the following line where they fit, the way the example programs are written
      const merged = [];
      for (let i = 0; i < lines.length; i++) {
        const x = lines[i], next = lines[i + 1];
        if (x.kind === "label" && next && next.kind === "ins" && x.text.length < 8) {
          merged.push({ ...next, text: x.text.padEnd(8) + next.text.slice(8), label: x.name, line: next.line || x.line });
          i++;
        } else merged.push(x);
      }
      const text = merged.map((x) => x.text).join("\n") + "\n";
      return { ok: true, asm: text, lines: merged, runtime: [...gen.runtime, ...(gen.needsHeap ? ["__New", "__Collect"] : [])] };
    } catch (e) {
      if (e instanceof CompileError) return { ok: false, error: { message: e.message, line: e.line, col: e.col } };
      throw e;
    }
  }

  const api = { compile, lex, CompileError };
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  else root.SharpC = api;
})(typeof window !== "undefined" ? window : globalThis);

})();
(function(){
// SHARP-16: an ExuArch machine built as a target for a small C# compiler.
// Accumulator A, second operand B, a stack with a frame pointer, and an
// address adder so locals and parameters are one instruction each.

const S = (...signals) => ({ signals });
const W = (when, ...signals) => ({ when, signals });

// Read the next program cell into a register: two ticks.
const operandTo = (target) => [S("pc.output", "mem.loadmar"), S("mem.output", "pc.inc", target)];

function ins(mnemonic, description, operandTypes, steps) {
  const def = { mnemonic, description, operands: operandTypes.length };
  if (operandTypes.length) def.operandTypes = operandTypes;
  def.steps = steps;
  return def;
}

// A conditional jump. `taken` and `skipped` are lists of flag conditions;
// each one gets its own copy of the steps, as the decoder needs one
// condition per step.
function jump(mnemonic, description, taken, skipped) {
  const steps = [];
  for (const when of taken) {
    steps.push(W(when, "pc.output", "mem.loadmar"));
    steps.push(W(when, "mem.output", "pc.load", "ir.reset"));
  }
  for (const when of skipped) steps.push(W(when, "pc.inc", "ir.reset"));
  return ins(mnemonic, description, ["address"], steps);
}

const aluOp = (mnemonic, line, description) => ins(mnemonic, description, [], [S(`alu.${line}`, "acc.load", "ir.reset")]);

function instructions() {
  return [
    ins("NOP", "Do nothing", [], [S("ir.reset")]),
    ins("HLT", "Stop the clock", [], [S("clk.disable")]),

    // ---- Loading and storing A
    ins("LDI", "A = value", ["value"], [S("pc.output", "mem.loadmar"), S("mem.output", "pc.inc", "acc.load", "ir.reset")]),
    ins("LEA", "A = the address of a label", ["address"], [S("pc.output", "mem.loadmar"), S("mem.output", "pc.inc", "acc.load", "ir.reset")]),
    ins("LD", "A = memory at the address (a global variable)", ["address"], [
      S("pc.output", "mem.loadmar"), S("mem.output", "pc.inc", "mem.loadmar"), S("mem.output", "acc.load", "ir.reset")]),
    ins("ST", "Memory at the address = A", ["address"], [
      S("pc.output", "mem.loadmar"), S("mem.output", "pc.inc", "mem.loadmar"), S("acc.output", "mem.load", "ir.reset")]),
    ins("LDL", "A = memory at FP + offset (a local or a parameter)", ["value"], [
      ...operandTo("off.load"), S("agu.add", "mem.loadmar"), S("mem.output", "acc.load", "ir.reset")]),
    ins("STL", "Memory at FP + offset = A", ["value"], [
      ...operandTo("off.load"), S("agu.add", "mem.loadmar"), S("acc.output", "mem.load", "ir.reset")]),
    ins("LEAL", "A = FP + offset, the address of a local", ["value"], [
      ...operandTo("off.load"), S("agu.add", "acc.load", "ir.reset")]),
    ins("LDX", "A = memory at the address in A", [], [S("acc.output", "mem.loadmar"), S("mem.output", "acc.load", "ir.reset")]),
    ins("STX", "Memory at the address in B = A", [], [S("tmp.output", "mem.loadmar"), S("acc.output", "mem.load", "ir.reset")]),
    ins("TAB", "B = A", [], [S("acc.output", "tmp.load", "ir.reset")]),
    ins("TBA", "A = B", [], [S("tmp.output", "acc.load", "ir.reset")]),

    // ---- The stack: SP points at the top item and the stack grows down
    ins("PUSH", "Push A", [], [S("sp.dec"), S("sp.output", "mem.loadmar"), S("acc.output", "mem.load", "ir.reset")]),
    ins("POP", "Pop into B", [], [S("sp.output", "mem.loadmar", "sp.inc"), S("mem.output", "tmp.load", "ir.reset")]),

    // ---- Arithmetic: A = B op A, setting the flags
    aluOp("ADD", "add", "A = B + A; sets the flags"),
    aluOp("SUB", "sub", "A = B - A; sets the flags"),
    aluOp("AND", "and", "A = B AND A; sets the flags"),
    aluOp("OR", "orr", "A = B OR A; sets the flags"),
    aluOp("XOR", "eor", "A = B XOR A; sets the flags"),
    aluOp("SHL", "lsl", "A = B shifted left by A; sets the flags"),
    aluOp("SHR", "lsr", "A = B shifted right by A, zeros in from the top; sets the flags"),
    ins("CMP", "Set the flags from B - A", [], [S("alu.cmp", "ir.reset")]),
    ins("TST", "Set Z and N from A (B is cleared)", [], [S("tmp.reset"), S("alu.orr", "acc.load", "ir.reset")]),
    ins("INC", "A = A + 1; flags unchanged", [], [S("acc.inc", "ir.reset")]),
    ins("DEC", "A = A - 1; flags unchanged", [], [S("acc.dec", "ir.reset")]),
    ins("NEG", "A = -A (B is cleared); sets the flags", [], [S("tmp.reset"), S("alu.sub", "acc.load", "ir.reset")]),
    ins("NOT", "A = NOT A, every bit flipped (B = 0xFFFF); sets the flags", [], [S("tmp.reset"), S("tmp.dec"), S("alu.eor", "acc.load", "ir.reset")]),
    ins("MUL", "A = B * A, signed, saturating at -32768 and 32767; flags unchanged", [], [
      S("tmp.output", "mac.loada"), S("acc.output", "mac.loadb", "mac.mul"), S("mac.output", "acc.load", "ir.reset")]),
    ins("DIV", "A = B / A, signed, rounding toward 0 like C#; flags unchanged", [], [
      S("tmp.output", "mac.loada", "off.reset"), S("off.inc"),
      S("off.output", "mac.loadb", "mac.mul"),
      S("acc.output", "mac.loadb", "mac.div"),
      S("mac.output", "acc.load", "ir.reset")]),
    ins("MOD", "A = B % A, with the sign of B like C#; sets the flags", [], [
      S("tmp.output", "mac.loada", "off.reset"), S("off.inc"),
      S("off.output", "mac.loadb", "mac.mul"),
      S("acc.output", "mac.loadb", "mac.div"),
      S("mac.output", "mac.loada"),
      S("mac.mul"),
      S("mac.output", "acc.load"),
      S("alu.sub", "acc.load", "ir.reset")]),

    // ---- Jumps. After CMP the flags describe B - A, signed
    ins("JMP", "Jump to the address", ["address"], [S("pc.output", "mem.loadmar"), S("mem.output", "pc.load", "ir.reset")]),
    jump("JEQ", "Jump if equal (Z set)", [{ Z: true }], [{ Z: false }]),
    jump("JNE", "Jump if not equal (Z clear)", [{ Z: false }], [{ Z: true }]),
    jump("JLT", "Jump if less, signed (N differs from V)",
      [{ N: true, V: false }, { N: false, V: true }], [{ N: false, V: false }, { N: true, V: true }]),
    jump("JGE", "Jump if greater or equal, signed (N equals V)",
      [{ N: false, V: false }, { N: true, V: true }], [{ N: true, V: false }, { N: false, V: true }]),
    jump("JGT", "Jump if greater, signed (Z clear and N equals V)",
      [{ Z: false, N: false, V: false }, { Z: false, N: true, V: true }],
      [{ Z: true }, { Z: false, N: true, V: false }, { Z: false, N: false, V: true }]),
    jump("JLE", "Jump if less or equal, signed (Z set or N differs from V)",
      [{ Z: true }, { Z: false, N: true, V: false }, { Z: false, N: false, V: true }],
      [{ Z: false, N: false, V: false }, { Z: false, N: true, V: true }]),

    // ---- Calls: CALL pushes the return address, ENTER builds a frame, RET k tears it down
    ins("CALL", "Push the address after this instruction and jump to the label", ["address"], [
      S("pc.output", "mem.loadmar", "sp.dec"),
      S("mem.output", "off.load", "pc.inc"),
      S("sp.output", "mem.loadmar"),
      S("pc.output", "mem.load"),
      S("off.output", "pc.load", "ir.reset")]),
    ins("ENTER", "Push FP, point FP at it and make room for n locals below it", ["value"], [
      S("sp.dec", "pc.output", "mem.loadmar"),
      S("mem.output", "off.load", "pc.inc"),
      S("sp.output", "mem.loadmar"),
      S("fp.output", "mem.load"),
      S("sp.output", "fp.load"),
      S("agu.sub", "sp.load", "ir.reset")]),
    ins("RET", "Return from an ENTER frame: SP = FP + k, then restore FP and jump back. k is 2 + the number of parameters", ["value"], [
      ...operandTo("off.load").map((s, i) => i === 1 ? S("mem.output", "off.load") : s),
      S("agu.add", "sp.load"),
      S("fp.output", "mem.loadmar"),
      S("mem.output", "fp.load", "mem.incmar"),
      S("mem.output", "pc.load", "ir.reset")]),

    // ---- Input and output
    ins("OUT", "Print the low 8 bits of A on the LCD", [], [S("acc.output", "lcd.load", "ir.reset")]),
    ins("CLT", "Clear the LCD", [], [S("lcd.clear", "ir.reset")]),
    ins("PX", "Screen cursor column = A", [], [S("acc.output", "fb.loadx", "ir.reset")]),
    ins("PY", "Screen cursor row = A", [], [S("acc.output", "fb.loady", "ir.reset")]),
    ins("PLOT", "Plot the RGB565 colour in A at the cursor, which then moves right", [], [S("acc.output", "fb.plot", "ir.reset")]),
    ins("CLS", "Clear the screen", [], [S("fb.clear", "ir.reset")]),
    ins("IN", "A = the keys held: 1 up, 2 down, 4 left, 8 right, 16 space", [], [S("keys.output", "acc.load", "ir.reset")]),
  ];
}

function machine() {
  const dev = (id, type, name, x, y, extra = {}) => ({ id, type, name, ...(type === "clock" ? {} : { bus: "main" }), ...extra, layout: { x, y } });
  const top = 40, bottom = 278;
  return {
    name: "SHARP-16",
    buses: [{ id: "main", layout: { x: 0, y: 222 } }],
    devices: [
      dev("pc", "register", "PC", 30, top),
      dev("mem", "ram", "MEMORY", 240, top, { parameters: { size: 65536 } }),
      dev("sp", "register", "SP", 450, top),
      dev("fp", "register", "FP", 660, top),
      dev("off", "register", "OFFSET", 870, top),
      dev("agu", "alu", "ADDRESS ADDER", 1080, top, { connections: { a: "fp", b: "off", status: "aflags" } }),
      dev("aflags", "register", "ADDER FLAGS", 1290, top),
      dev("lcd", "display", "LCD", 1500, top, { parameters: { columns: 32, rows: 8 } }),
      dev("fb", "framebuffer", "SCREEN", 1710, top),
      dev("ir", "instructionRegister", "IR", 30, bottom),
      dev("acc", "register", "A", 240, bottom),
      dev("tmp", "register", "B", 450, bottom),
      dev("alu", "alu", "ALU", 660, bottom, { connections: { a: "tmp", b: "acc", status: "flags" } }),
      dev("flags", "statusRegister", "FLAGS", 870, bottom),
      dev("mac", "mac", "MULTIPLIER", 1080, bottom, { parameters: { shift: 0 } }),
      dev("keys", "keypad", "KEYPAD", 1290, bottom),
      dev("clk", "clock", "CLOCK", 1500, bottom),
    ],
    decoder: {
      status: "flags",
      instructionRegister: "ir",
      microcode: {
        name: "SHARP-16 microcode",
        fetch: {
          mnemonic: "FETCH",
          description: "Load the opcode at the program counter and step past it",
          steps: [S("pc.output", "mem.loadmar"), S("mem.output", "ir.load", "pc.inc")],
        },
        instructions: instructions(),
      },
      layout: { x: 1920, y: top },
    },
    halt: "clk",
    programMemory: "mem",
  };
}

const README = `# SHARP-16

A machine built to be the target of a compiler. It runs a small subset of C#: \`static\` methods with parameters, locals and recursion, \`int\`, \`bool\`, \`char\` and \`string\`, arrays, \`if\`, loops and \`switch\`, and \`Console.Write\` to the LCD. The compiler and a live editor that shows the assembly as you type come with it.

## The idea

A compiler wants three things from a machine: somewhere to work out an expression, somewhere to keep a function's variables, and a cheap way to call and return. SHARP-16 has one of each.

- **An accumulator and a second operand.** Every expression ends up in [A](exuarch:device/acc). A binary operation works on B and A and leaves the result in A: \`ADD\` is A = B + A. The compiler puts the left side in B with [TAB](exuarch:instruction/TAB) when the right side is simple, and on the stack with [PUSH](exuarch:instruction/PUSH) and [POP](exuarch:instruction/POP) when it is not.
- **A frame pointer and an address adder.** A function's parameters and locals sit at fixed distances from [FP](exuarch:device/fp), so the compiler knows each one's offset. [LDL](exuarch:instruction/LDL) and [STL](exuarch:instruction/STL) take that offset and the [ADDRESS ADDER](exuarch:device/agu), a second ALU wired to FP and OFFSET, works out the address without touching A, B or the flags.
- **Calls that build their own frames.** [CALL](exuarch:instruction/CALL) pushes the return address, [ENTER](exuarch:instruction/ENTER) n saves FP and makes room for n locals, and [RET](exuarch:instruction/RET) k puts everything back and drops the k - 2 arguments the caller pushed.

## A frame

The stack grows down from the top of memory and SP points at the top item. Every register starts at 0, so the first push wraps SP round to 65535.

| Address | Holds |
|---|---|
| FP + 2 ... | the arguments, the last one nearest |
| FP + 1 | the return address |
| FP | the caller's FP |
| FP - 1 ... | the locals |

For \`static int Add(int a, int b)\`, \`a\` is at FP + 3 and \`b\` at FP + 2, and the method ends with \`RET 4\`.

## Arithmetic

[MUL](exuarch:instruction/MUL), [DIV](exuarch:instruction/DIV) and [MOD](exuarch:instruction/MOD) use the [MULTIPLIER](exuarch:device/mac), with its shift at 0 so it works on whole numbers. Division rounds toward 0 and the remainder takes the sign of the dividend, as in C#. A product that does not fit in 16 bits saturates at -32768 or 32767 instead of wrapping, and dividing by 0 gives 32767 or -32768 rather than an exception.

The jumps after [CMP](exuarch:instruction/CMP) are signed: [JLT](exuarch:instruction/JLT) jumps when N and V differ, [JGT](exuarch:instruction/JGT) when Z is clear and N and V agree.

## Things to try

1. Open a program, switch on ⚡ Max and run it.
2. Step through \`CALL\`, \`ENTER\` and \`RET\` and watch SP and FP build and take down a frame.
3. Write C# in the SHARP-16 editor, download the package with your program in it and import it here.
`;

if (typeof module !== "undefined" && module.exports) module.exports = { machine, README };
else globalThis.Sharp16 = { machine, README };

})();
(function(){
// Builds an ExuArch package (the file Import… reads) for SHARP-16.
(function (root) {
  "use strict";
  const DESCRIPTION = "An accumulator machine built as a compiler target: A and B for expressions, a stack with a frame pointer and an address adder for locals and parameters, CALL, ENTER and RET k for calls, and a multiplier for *, / and %. Its programs are compiled from a subset of C#.";

  function programSource(csharp, asm) {
    const header = ["; Compiled from C# by SharpC for SHARP-16", ";", ...csharp.replace(/\s+$/, "").split(/\r?\n/).map((l) => (";   " + l).trimEnd()), ""];
    return header.join("\n") + "\n" + asm.replace(/\s+$/, "");
  }

  function makePackage(machine, readme, programs) {
    return {
      formatVersion: 1,
      name: "SHARP-16",
      description: DESCRIPTION,
      tagline: "A target for compiled C#",
      tags: ["compiler", "stack", "graphics"],
      preview: programs.some((p) => p.name === "Sierpinski on the screen") ? "Sierpinski on the screen" : programs[0] && programs[0].name,
      readme,
      machine,
      programs: programs.map((p) => {
        const out = { name: p.name, description: p.description, source: programSource(p.csharp, p.asm) };
        if (p.needs) out.needs = p.needs;
        return out;
      }),
    };
  }

  const api = { makePackage, programSource };
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  else root.Sharp16Package = api;
})(typeof window !== "undefined" ? window : globalThis);

})();
;
const EXAMPLES = [{"name":"Hello and Fibonacci","description":"Recursion through CALL, ENTER and RET, and an interpolated string","csharp":"using System;\n\nclass Program\n{\n    static int Fib(int n) => n < 2 ? n : Fib(n - 1) + Fib(n - 2);\n\n    static void Main()\n    {\n        Console.WriteLine(\"Hello from C# on SHARP-16!\");\n        for (int i = 0; i < 15; i++)\n            Console.Write($\"{Fib(i)} \");\n    }\n}\n"},{"name":"FizzBuzz","description":"A switch on i % 15 turned into a chain of CMP and JEQ","csharp":"using System;\n\nclass FizzBuzz\n{\n    static void Main()\n    {\n        for (int i = 1; i <= 30; i++)\n        {\n            switch (i % 15)\n            {\n                case 0: Console.Write(\"FizzBuzz\"); break;\n                case 3: case 6: case 9: case 12: Console.Write(\"Fizz\"); break;\n                case 5: case 10: Console.Write(\"Buzz\"); break;\n                default: Console.Write(i); break;\n            }\n            Console.Write(' ');\n        }\n    }\n}\n"},{"name":"Sieve of Eratosthenes","description":"A bool array made with new, crossed off in place","csharp":"using System;\n\nclass Sieve\n{\n    const int Limit = 200;\n\n    static void Main()\n    {\n        bool[] composite = new bool[Limit];\n        int count = 0;\n        for (int n = 2; n < Limit; n++)\n        {\n            if (composite[n]) continue;\n            Console.Write(n);\n            Console.Write(' ');\n            count++;\n            for (int m = n * n; m < Limit && m > 0; m += n)\n                composite[m] = true;\n        }\n        Console.WriteLine();\n        Console.WriteLine($\"{count} primes below {Limit}\");\n    }\n}\n"},{"name":"Insertion sort","description":"An array initializer, a method that changes the array it is given, and foreach","csharp":"using System;\n\nclass Sorting\n{\n    static void InsertionSort(int[] a)\n    {\n        for (int i = 1; i < a.Length; i++)\n        {\n            int key = a[i];\n            int j = i - 1;\n            while (j >= 0 && a[j] > key)\n            {\n                a[j + 1] = a[j];\n                j--;\n            }\n            a[j + 1] = key;\n        }\n    }\n\n    static void Print(string label, int[] a)\n    {\n        Console.Write(label);\n        foreach (int x in a)\n            Console.Write($\" {x}\");\n        Console.WriteLine();\n    }\n\n    static void Main()\n    {\n        int[] numbers = { 42, -7, 19, 0, 3, 1000, -250, 8, 8, 77 };\n        Print(\"before:\", numbers);\n        InsertionSort(numbers);\n        Print(\"after: \", numbers);\n        Console.WriteLine($\"min {numbers[0]}, max {numbers[numbers.Length - 1]}\");\n    }\n}\n"},{"name":"Sierpinski on the screen","description":"A pixel wherever x AND y is 0, coloured with Screen.Rgb","csharp":"// A Sierpinski triangle: a pixel is lit where x AND y has no bits set\nusing System;\n\nclass Sierpinski\n{\n    const int Size = 256;\n\n    static void Main()\n    {\n        Screen.Clear();\n        for (int y = 0; y < Size; y++)\n        {\n            Screen.MoveTo(192, y + 112);\n            for (int x = 0; x < Size; x++)\n            {\n                int colour = (x & y) == 0 ? Screen.Rgb(x, y, 255 - x) : 0;\n                Screen.Put(colour);\n            }\n        }\n        Console.Write(\"Sierpinski, drawn\");\n    }\n}\n"},{"name":"A tree of objects","description":"Classes with fields, a constructor and methods, made with new; 20000 throwaway nodes make the garbage collector run","csharp":"using System;\n\nclass Node\n{\n    int value;\n    Node left, right;\n\n    public Node(int v) { value = v; }\n\n    public void Insert(int v)\n    {\n        if (v < value)\n        {\n            if (left == null) left = new Node(v); else left.Insert(v);\n        }\n        else\n        {\n            if (right == null) right = new Node(v); else right.Insert(v);\n        }\n    }\n\n    public void Print()\n    {\n        if (left != null) left.Print();\n        Console.Write($\"{value} \");\n        if (right != null) right.Print();\n    }\n}\n\nclass Program\n{\n    static void Main()\n    {\n        Node tree = new Node(50);\n        int[] values = { 30, 70, 20, 40, 60, 80, 35, 65, 10 };\n        foreach (int v in values) tree.Insert(v);\n        tree.Print();\n        Console.WriteLine();\n\n        for (int i = 0; i < 20000; i++) new Node(i);\n        Console.WriteLine(\"20000 throwaway nodes later,\");\n        Console.WriteLine(\"the collector kept the tree:\");\n        tree.Print();\n    }\n}\n"},{"name":"Sketch with the arrow keys","description":"Keys.Read in a loop that never ends; switch on Max speed","needs":{"keypad":true,"minHz":50000},"csharp":"// Draw with the arrow keys; space changes the colour.\n// Switch on Max speed and use the keypad.\nusing System;\n\nclass Sketch\n{\n    static int[] palette = { 0xFFFF, 0xF800, 0x07E0, 0x001F, 0xFFE0 };\n\n    static void Main()\n    {\n        int x = 320, y = 240, colour = 0;\n        Console.WriteLine(\"Arrow keys draw,\");\n        Console.WriteLine(\"space changes colour\");\n        while (true)\n        {\n            int keys = Keys.Read();\n            if ((keys & Keys.Left) != 0 && x > 0) x--;\n            if ((keys & Keys.Right) != 0 && x < 639) x++;\n            if ((keys & Keys.Up) != 0 && y > 0) y--;\n            if ((keys & Keys.Down) != 0 && y < 479) y++;\n            if ((keys & Keys.Space) != 0) colour = (colour + 1) % palette.Length;\n            Screen.Plot(x, y, palette[colour]);\n        }\n    }\n}\n"}];
;

(function () {
  "use strict";
  const $ = (id) => document.getElementById(id);
  const def = Sharp16.machine();
  const src = $("src"), hl = $("hl"), gutter = $("gutter"), asmBox = $("asm");
  let compiled = null;     // last good compile
  let lastError = null;
  let machine = null, running = false, rafId = 0;
  let asmRows = [];        // DOM rows of the assembly
  let addrToRow = new Map();
  let hotLine = 0;

  // ---------------------------------------------------------------- syntax colouring
  const esc = (s) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
  const CS_KW = /^(using|namespace|class|static|public|private|internal|protected|readonly|const|void|int|short|bool|char|string|var|if|else|while|do|for|foreach|in|switch|case|default|break|continue|return|true|false|new|null|this|partial)$/;
  const CS_TYPES = /^(Console|Screen|Keys|Math|Machine|Environment|GC)$/;
  function csTokens(text) {
    const re = /(\/\/[^\n]*|\/\*[\s\S]*?(?:\*\/|$))|(\$?@?"(?:[^"\\\n]|\\.)*"?|'(?:[^'\\\n]|\\.)*'?)|(\b0[xXbB][0-9A-Fa-f_]+\b|\b\d+\b)|([A-Za-z_][A-Za-z0-9_]*)/g;
    const tokens = [];
    let last = 0, m;
    while ((m = re.exec(text))) {
      if (m.index > last) tokens.push({ text: text.slice(last, m.index) });
      tokens.push({ cls: csTokenClass(m), text: m[0] });
      last = re.lastIndex;
    }
    if (last < text.length) tokens.push({ text: text.slice(last) });
    return tokens;
  }
  function csTokenClass(m) {
    if (m[1]) return "c";
    if (m[2]) return "s";
    if (m[3]) return "n";
    if (CS_KW.test(m[4])) return "k";
    if (CS_TYPES.test(m[4])) return "t";
    return null;
  }
  function tokenNode(token) {
    if (!token.cls) return document.createTextNode(token.text);
    const span = document.createElement("span");
    span.className = token.cls;
    span.textContent = token.text;
    return span;
  }
  function colourAsm(line) {
    let code = line, comment = "";
    const sc = indexOfComment(line);
    if (sc >= 0) { code = line.slice(0, sc); comment = line.slice(sc); }
    let html = "";
    const lm = code.match(/^([A-Za-z_.][A-Za-z0-9_]*:)(.*)$/);
    let rest = code;
    if (lm) { html += `<span class="lb">${esc(lm[1])}</span>`; rest = lm[2]; }
    const mm = rest.match(/^(\s*)(\.?[A-Za-z]+)(.*)$/);
    if (mm) {
      html += esc(mm[1]) + `<span class="${mm[2].startsWith(".") ? "d" : "m"}">${esc(mm[2])}</span>`;
      html += esc(mm[3]).replace(/(&quot;|")((?:[^"\\]|\\.)*)"/g, '<span class="s">"$2"</span>').replace(/('(?:[^'\\]|\\.)')/g, '<span class="s">$1</span>').replace(/(^|[\s,])(-?\d+|0x[0-9A-Fa-f]+)(?=$|[\s,])/g, '$1<span class="n">$2</span>');
    } else html += esc(rest);
    if (comment) html += `<span class="c">${esc(comment)}</span>`;
    return html;
  }
  function indexOfComment(line) {
    let q = null;
    for (let i = 0; i < line.length; i++) {
      const c = line[i];
      if (q) { if (c === "\\") i++; else if (c === q) q = null; }
      else if (c === '"' || c === "'") q = c;
      else if (c === ";") return i;
    }
    return -1;
  }

  // ---------------------------------------------------------------- editor
  function renderEditor() {
    const lines = src.value.split("\n");
    const errLine = lastError ? lastError.line : 0;
    hl.replaceChildren(...lines.map((l, i) => editorLine(l, i + 1, errLine)));
    let g = "";
    for (let i = 1; i <= lines.length; i++) g += `<div class="${i === errLine ? "err" : i === hotLine ? "hot" : ""}">${i}</div>`;
    gutter.innerHTML = g;
    syncScroll();
  }
  function editorLine(text, lineNumber, errLine) {
    const line = document.createElement("span");
    line.className = "ln";
    if (lineNumber === hotLine) line.classList.add("hot");
    if (lineNumber === errLine) line.classList.add("err");
    const tokens = csTokens(text);
    if (tokens.length) line.append(...tokens.map(tokenNode));
    else line.textContent = " ";
    return line;
  }
  function syncScroll() {
    hl.scrollTop = src.scrollTop; hl.scrollLeft = src.scrollLeft;
    gutter.scrollTop = src.scrollTop;
  }
  src.addEventListener("scroll", syncScroll);
  src.addEventListener("keydown", (e) => {
    if (e.key === "Tab" && !e.shiftKey && !e.metaKey && !e.ctrlKey) {
      e.preventDefault();
      if (document.execCommand) document.execCommand("insertText", false, "    ");
    } else if (e.key === "Enter" && !e.metaKey && !e.ctrlKey) {
      const before = src.value.slice(0, src.selectionStart);
      const indent = (before.split("\n").pop().match(/^\s*/) || [""])[0] + (/\{\s*$/.test(before) ? "    " : "");
      e.preventDefault();
      document.execCommand("insertText", false, "\n" + indent);
    }
  });

  let timer = 0;
  src.addEventListener("input", () => {
    renderEditor();
    clearTimeout(timer);
    timer = setTimeout(() => { compileNow(); saveDraft(); }, 140);
  });
  ["keyup", "click", "select"].forEach((ev) => src.addEventListener(ev, followCaret));
  function followCaret() {
    const line = src.value.slice(0, src.selectionStart).split("\n").length;
    if (line === hotLine) return;
    setHot(line, true);
  }
  function setHot(line, scrollAsm) {
    hotLine = line;
    renderEditor();
    let first = null;
    for (const r of asmRows) {
      const on = r.srcLine === line && line > 0;
      r.el.classList.toggle("hot", on);
      if (on && !first) first = r.el;
    }
    if (scrollAsm && first) scrollIntoViewWithin(asmBox, first);
  }
  function scrollIntoViewWithin(box, el) {
    const top = el.offsetTop - box.offsetTop, h = box.clientHeight;
    if (top < box.scrollTop + 20 || top > box.scrollTop + h - 40) box.scrollTop = Math.max(0, top - h / 3);
  }

  // ---------------------------------------------------------------- compiling
  function compileNow() {
    const r = SharpC.compile(src.value);
    const err = $("err"), stat = $("cstat");
    if (!r.ok) {
      lastError = r.error;
      err.hidden = false;
      err.innerHTML = `<b>line ${r.error.line}${r.error.col ? ":" + r.error.col : ""}</b>${esc(r.error.message)}`;
      stat.textContent = "error"; stat.classList.add("bad");
      asmBox.classList.add("stale");
      renderEditor();
      return;
    }
    let program;
    try { program = ExuSim.assemble(r.asm, new ExuSim.DecoderRom(def.decoder.microcode)); }
    catch (e) { program = { errors: [{ line: 0, message: e.message }] }; }
    if (program.errors.length) {
      lastError = { line: 0, message: "assembler: " + program.errors[0].message };
      err.hidden = false; err.textContent = lastError.message;
      stat.textContent = "error"; stat.classList.add("bad");
      return;
    }
    lastError = null;
    err.hidden = true;
    stat.textContent = "compiled"; stat.classList.remove("bad");
    asmBox.classList.remove("stale");
    compiled = { ...r, program };
    renderAsm();
    renderEditor();
    const instr = program.listing.filter((l) => l.instruction).length;
    $("ameta").textContent = `${program.cells.length} cells · ${instr} instructions`;
    $("cmeta").textContent = r.runtime.length ? `runtime: ${r.runtime.join(" ")}` : "";
    if ($("auto").checked) start(true);
    else resetMachine();
  }
  function renderAsm() {
    const lines = compiled.asm.replace(/\n$/, "").split("\n");
    addrToRow = new Map();
    const byLine = new Map();
    for (const l of compiled.program.listing) byLine.set(l.line, l);
    const frag = document.createDocumentFragment();
    asmRows = lines.map((text, i) => {
      const row = document.createElement("div");
      row.className = "row";
      const lst = byLine.get(i + 1);
      const addr = lst && lst.cells.length ? lst.address.toString(16).toUpperCase().padStart(4, "0") : "";
      row.innerHTML = `<span class="addr">${addr}</span><span class="txt">${colourAsm(text) || " "}</span>`;
      const srcLine = compiled.lines[i] ? compiled.lines[i].line : 0;
      const r = { el: row, srcLine };
      if (lst && lst.instruction) addrToRow.set(lst.address, r);
      if (srcLine) {
        row.addEventListener("mouseenter", () => setHot(srcLine, false));
      }
      frag.appendChild(row);
      return r;
    });
    asmBox.replaceChildren(frag);
    for (const r of asmRows) r.el.classList.toggle("hot", hotLine > 0 && r.srcLine === hotLine);
  }

  // ---------------------------------------------------------------- the machine
  const ctx = $("screen").getContext("2d");
  const image = ctx.createImageData(640, 480);
  const LUT_R = new Uint8Array(32), LUT_G = new Uint8Array(64);
  for (let i = 0; i < 32; i++) LUT_R[i] = (i * 527 + 23) >> 6;
  for (let i = 0; i < 64; i++) LUT_G[i] = (i * 259 + 33) >> 6;
  function clearCanvas() { image.data.fill(0); for (let i = 3; i < image.data.length; i += 4) image.data[i] = 255; ctx.putImageData(image, 0, 0); }

  function resetMachine() {
    stop();
    machine = null;
    clearCanvas();
    if (compiled) {
      try { machine = new ExuSim.Machine(def, compiled.asm); }
      catch (e) { $("mmeta").textContent = e.message; return; }
    }
    keys.held = 0;
    paint(true);
    $("mmeta").textContent = "ready";
  }
  function start(fresh) {
    if (fresh || !machine || machine.halted) resetMachine();
    if (!machine) return;
    running = true;
    $("run").textContent = "Pause";
    clearPc();
    let lastT = performance.now();
    const loop = (t) => {
      if (!running) return;
      const hz = +$("speed").value;
      let budget;
      if (hz === 0) budget = 400000; else { budget = Math.max(1, Math.round(hz * Math.min(0.1, (t - lastT) / 1000))); }
      lastT = t;
      const t0 = performance.now();
      let n = 0;
      try {
        while (n < budget && !machine.halted) {
          machine.step(); n++;
          if (hz === 0 && (n & 8191) === 0 && performance.now() - t0 > 14) break;
        }
      } catch (e) { running = false; $("mmeta").textContent = "stopped: " + e.message; $("run").textContent = "Run"; return; }
      paint(false);
      if (machine.halted) { stop(); $("mmeta").textContent = `halted after ${fmt(machine.cycles)} ticks`; return; }
      rafId = requestAnimationFrame(loop);
    };
    rafId = requestAnimationFrame(loop);
  }
  function stop() { running = false; cancelAnimationFrame(rafId); $("run").textContent = "Run"; }
  const fmt = (n) => n.toLocaleString("en-US");
  const hex = (v) => (v & 0xffff).toString(16).toUpperCase().padStart(4, "0");
  const sgn = (v) => ((v & 0xffff) ^ 0x8000) - 0x8000;

  function paint(full) {
    if (!machine) { $("lcd").textContent = " ".repeat(32) + "\n".repeat(7); $("regs").innerHTML = ""; return; }
    $("lcd").textContent = machine.device("lcd").text();
    const fb = machine.device("fb");
    if (full) fb.dirty = [0, 0, 639, 479];
    if (fb.dirty) {
      const [l, t, r, b] = fb.dirty;
      const px = fb.pixels, d = image.data;
      for (let y = t; y <= b; y++) for (let x = l; x <= r; x++) {
        const v = px[y * 640 + x], o = (y * 640 + x) * 4;
        d[o] = LUT_R[v >> 11]; d[o + 1] = LUT_G[(v >> 5) & 63]; d[o + 2] = LUT_R[v & 31]; d[o + 3] = 255;
      }
      ctx.putImageData(image, 0, 0, l, t, r - l + 1, b - t + 1);
      fb.dirty = null;
    }
    $("fbinfo").textContent = `cursor ${fb.x}, ${fb.y}`;
    const reg = (id) => machine.device(id).data;
    const f = reg("flags");
    const flag = (bit, name) => (f & bit ? `<b>${name}</b>` : `<s>${name}</s>`);
    $("regs").innerHTML = [
      ["PC", hex(reg("pc"))], ["A", `${hex(reg("acc"))} <i>${sgn(reg("acc"))}</i>`], ["B", `${hex(reg("tmp"))} <i>${sgn(reg("tmp"))}</i>`],
      ["SP", hex(reg("sp"))], ["FP", hex(reg("fp"))],
    ].map(([n, v]) => `<div class="reg"><span>${n}</span><code>${v}</code></div>`).join("")
      + `<div class="reg flags"><span>FLAGS</span><code>${flag(8, "N")}${flag(4, "V")}${flag(2, "C")}${flag(1, "Z")}</code></div>`
      + `<div class="reg"><span>TICKS</span><code>${fmt(machine.cycles)}</code></div>`;
    if (running) $("mmeta").textContent = `running · ${fmt(machine.cycles)} ticks`;
  }
  function clearPc() { for (const r of asmRows) r.el.classList.remove("pc"); }
  function stepInstruction() {
    if (!compiled) return;
    if (!machine || machine.halted) resetMachine();
    stop();
    let n = 0;
    // run until the next opcode has been fetched; that instruction is shown as the one about to run
    const before = machine.instructions;
    while (!machine.halted && n < 100000) { machine.step(); n++; if (machine.instructions > before) break; }
    paint(false);
    clearPc();
    const at = machine.currentAddress;
    const row = addrToRow.get(at);
    if (row) { row.el.classList.add("pc"); scrollIntoViewWithin(asmBox, row.el); if (row.srcLine) { hotLine = row.srcLine; renderEditor(); } }
    $("mmeta").textContent = machine.halted ? `halted after ${fmt(machine.cycles)} ticks` : `paused at ${hex(at)} · ${fmt(machine.cycles)} ticks`;
  }

  $("run").addEventListener("click", () => { if (running) { stop(); $("mmeta").textContent = `paused · ${fmt(machine.cycles)} ticks`; } else start(false); });
  $("reset").addEventListener("click", () => { resetMachine(); clearPc(); });
  $("step").addEventListener("click", stepInstruction);

  // ---------------------------------------------------------------- keypad
  const keys = { held: 0 };
  function setKey(bit, down) {
    const pad = machine && machine.device("keys");
    if (pad) { if (down) pad.press(bit); else pad.release(bit); }
    document.querySelectorAll(`#keypad [data-key="${bit}"]`).forEach((b) => b.classList.toggle("down", down));
  }
  document.querySelectorAll("#keypad .btn").forEach((b) => {
    const bit = +b.dataset.key;
    b.addEventListener("pointerdown", (e) => { e.preventDefault(); b.setPointerCapture(e.pointerId); setKey(bit, true); });
    b.addEventListener("pointerup", () => setKey(bit, false));
    b.addEventListener("pointercancel", () => setKey(bit, false));
  });
  const KEYMAP = { ArrowUp: 1, ArrowDown: 2, ArrowLeft: 4, ArrowRight: 8, " ": 16 };
  const screenEl = $("screen");
  screenEl.addEventListener("keydown", (e) => { if (e.key in KEYMAP) { e.preventDefault(); setKey(KEYMAP[e.key], true); } });
  screenEl.addEventListener("keyup", (e) => { if (e.key in KEYMAP) { e.preventDefault(); setKey(KEYMAP[e.key], false); } });
  screenEl.addEventListener("blur", () => { for (const b of [1, 2, 4, 8, 16]) setKey(b, false); });

  // ---------------------------------------------------------------- examples, copying, drafts
  const sel = $("example");
  EXAMPLES.forEach((ex, i) => { const o = document.createElement("option"); o.value = i; o.textContent = ex.name; sel.appendChild(o); });
  sel.addEventListener("change", () => { load(EXAMPLES[+sel.value].csharp); });
  function load(text) { src.value = text; hotLine = 0; src.scrollTop = 0; renderEditor(); compileNow(); saveDraft(); }
  function saveDraft() { try { localStorage.setItem("sharpc-draft", JSON.stringify({ text: src.value, example: sel.value })); } catch (e) {} }
  function readDraft() { try { return JSON.parse(localStorage.getItem("sharpc-draft") || "null"); } catch (e) { return null; } }

  function toast(text) { const t = $("toast"); t.textContent = text; t.hidden = false; clearTimeout(toast.t); toast.t = setTimeout(() => { t.hidden = true; }, 2600); }
  function copyText(text, title, help) {
    const fallback = () => {
      $("dlgTitle").textContent = title; $("dlgText").textContent = help;
      $("dialog").hidden = false; const a = $("dlgArea"); a.value = text; a.focus(); a.select();
    };
    try {
      if (!navigator.clipboard) return fallback();
      navigator.clipboard.writeText(text).then(() => toast(`${title} copied`), fallback);
    } catch (e) { fallback(); }
  }
  $("dlgClose").addEventListener("click", () => { $("dialog").hidden = true; });
  $("copyAsm").addEventListener("click", () => {
    if (!compiled) return toast("Fix the error first");
    copyText(compiled.asm, "Assembly", "Copying was blocked here. The assembly below is selected, copy it from here.");
  });
  $("copyPkg").addEventListener("click", () => {
    if (!compiled) return toast("Fix the error first");
    const mine = { name: "My program", description: "Compiled from C# in SharpC", csharp: src.value, asm: compiled.asm,
      needs: /Keys\.Read/.test(src.value) ? { keypad: true, minHz: 50000 } : undefined };
    const examples = EXAMPLES.map((ex) => ({ ...ex, asm: SharpC.compile(ex.csharp).asm }));
    const pkg = Sharp16Package.makePackage(def, Sharp16.README, [mine, ...examples]);
    copyText(JSON.stringify(pkg, null, 2), "Package", "Copying was blocked here. The package below is selected: copy it, save it as SHARP-16.json and use Import… in the Machines drawer at exuarch.com.");
  });

  const draft = readDraft();
  if (draft && draft.text) { if (draft.example) sel.value = draft.example; src.value = draft.text; }
  else src.value = EXAMPLES[0].csharp;
  renderEditor();
  compileNow();
})();
