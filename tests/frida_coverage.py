#!/usr/bin/env python3
"""
Frida-based coverage tracer for target.dll on macOS/Wine.

Compares to DynamoRIO drcov on Windows CI.

Usage:
    # Option 1: Trace running Wine process
    python3 frida_coverage.py --attach

    # Option 2: Spawn and trace
    python3 frida_coverage.py --spawn coverage_test_driver.exe ../dlls/original/target.dll test_files

    # Option 3: Just list exports
    python3 frida_coverage.py --list-exports ../dlls/original/target.dll
"""

import argparse
import json
import os
import subprocess
import sys
import time
from pathlib import Path
from datetime import datetime

try:
    import frida
except ImportError:
    print("[ERROR] Frida not installed. Run: pip3 install frida-tools")
    sys.exit(1)


# JavaScript instrumentation code for Frida
TRACE_SCRIPT = """
'use strict';

const TARGET_DLL = 'target.dll';
const coverage = {
    functions_called: {},
    call_count: 0,
    start_time: Date.now(),
    basic_blocks: []
};

// Find the DLL module
function findModule() {
    const modules = Process.enumerateModules();
    for (const mod of modules) {
        if (mod.name.toLowerCase().includes('target')) {
            return mod;
        }
    }
    return null;
}

// Hook all exports from target.dll
function hookExports(mod) {
    const exports = mod.enumerateExports();
    console.log('[Frida] Found ' + exports.length + ' exports in ' + mod.name);

    for (const exp of exports) {
        if (exp.type === 'function' && exp.name.startsWith('Aud_')) {
            try {
                Interceptor.attach(exp.address, {
                    onEnter: function(args) {
                        const name = exp.name;
                        if (!coverage.functions_called[name]) {
                            coverage.functions_called[name] = { count: 0, first_call: Date.now() };
                        }
                        coverage.functions_called[name].count++;
                        coverage.call_count++;

                        // Log first call to each function
                        if (coverage.functions_called[name].count === 1) {
                            console.log('[CALL] ' + name + ' (first call)');
                        }
                    },
                    onLeave: function(retval) {
                        // Could log return values here
                    }
                });
                console.log('[HOOK] ' + exp.name);
            } catch (e) {
                console.log('[SKIP] ' + exp.name + ': ' + e.message);
            }
        }
    }
}

// Use Stalker for basic block coverage (optional, heavier)
function enableStalker(mod) {
    const moduleBase = mod.base;
    const moduleSize = mod.size;

    Stalker.follow(Process.getCurrentThreadId(), {
        events: {
            compile: true,
            block: true
        },
        onReceive: function(events) {
            const dominated = Stalker.parse(events, {
                annotate: false,
                stringify: false
            });

            for (const ev of dominated) {
                if (ev[0] === 'block') {
                    const addr = ev[1];
                    // Check if block is in our target DLL
                    if (addr >= moduleBase && addr < moduleBase.add(moduleSize)) {
                        const offset = addr.sub(moduleBase).toInt32();
                        coverage.basic_blocks.push(offset);
                    }
                }
            }
        }
    });
    console.log('[Stalker] Enabled basic block tracing');
}

// Report coverage on detach
function reportCoverage() {
    const elapsed = (Date.now() - coverage.start_time) / 1000;
    const funcs = Object.keys(coverage.functions_called);

    console.log('\\n========================================');
    console.log('FRIDA COVERAGE REPORT');
    console.log('========================================');
    console.log('Elapsed time: ' + elapsed.toFixed(2) + 's');
    console.log('Total calls: ' + coverage.call_count);
    console.log('Unique functions: ' + funcs.length + '/28');
    console.log('');

    // Sort by call count
    funcs.sort((a, b) => coverage.functions_called[b].count - coverage.functions_called[a].count);

    for (const name of funcs) {
        const info = coverage.functions_called[name];
        console.log('  [OK] ' + name + ': ' + info.count + ' calls');
    }

    if (coverage.basic_blocks.length > 0) {
        console.log('');
        console.log('Basic blocks executed: ' + coverage.basic_blocks.length);
    }

    console.log('========================================');

    // Send data back to Python
    send({
        type: 'coverage',
        data: coverage
    });
}

// Main
rpc.exports = {
    init: function(useStalker) {
        console.log('[Frida] Initializing target.dll tracer...');

        // Wait for DLL to load
        let attempts = 0;
        const checkModule = setInterval(function() {
            const mod = findModule();
            if (mod) {
                clearInterval(checkModule);
                console.log('[Frida] Found module: ' + mod.name + ' at ' + mod.base);
                hookExports(mod);

                if (useStalker) {
                    enableStalker(mod);
                }
            } else {
                attempts++;
                if (attempts > 100) {
                    clearInterval(checkModule);
                    console.log('[Frida] Module not found after 10s');
                }
            }
        }, 100);
    },

    report: function() {
        reportCoverage();
    },

    getCoverage: function() {
        return coverage;
    }
};
"""


def list_exports(dll_path: str):
    """List exports from DLL using Wine's objdump or dumpbin."""
    print(f"\n=== Exports from {dll_path} ===\n")

    # Try objdump first
    try:
        result = subprocess.run(
            ["objdump", "-p", dll_path],
            capture_output=True, text=True, timeout=30
        )

        in_export = False
        exports = []
        for line in result.stdout.split('\n'):
            if 'Export Address Table' in line or '[Ordinal/Name Pointer]' in line:
                in_export = True
                continue
            if in_export and line.strip():
                if 'Aud_' in line:
                    # Extract function name
                    parts = line.split()
                    for p in parts:
                        if p.startswith('Aud_'):
                            exports.append(p)

        if exports:
            print(f"Found {len(exports)} Aud_* exports:")
            for exp in sorted(exports):
                print(f"  {exp}")
            return exports
    except Exception as e:
        print(f"objdump failed: {e}")

    # Fallback: use Wine's dumpbin or just read PE
    print("(Could not parse exports - ensure objdump is installed)")
    return []


def find_wine_pid():
    """Find running Wine process."""
    try:
        result = subprocess.run(
            ["pgrep", "-f", "wine.*coverage_test"],
            capture_output=True, text=True
        )
        pids = result.stdout.strip().split('\n')
        if pids and pids[0]:
            return int(pids[0])
    except:
        pass

    # Try broader search
    try:
        result = subprocess.run(
            ["pgrep", "-f", "wine"],
            capture_output=True, text=True
        )
        pids = result.stdout.strip().split('\n')
        if pids and pids[0]:
            print(f"Found Wine PIDs: {pids}")
            return int(pids[0])
    except:
        pass

    return None


def attach_and_trace(pid: int, use_stalker: bool = False):
    """Attach to process and trace target.dll calls."""
    print(f"\n[Frida] Attaching to PID {pid}...")

    coverage_data = None

    def on_message(message, data):
        nonlocal coverage_data
        if message['type'] == 'send':
            payload = message['payload']
            if payload.get('type') == 'coverage':
                coverage_data = payload['data']
        elif message['type'] == 'error':
            print(f"[ERROR] {message['stack']}")

    try:
        session = frida.attach(pid)
        script = session.create_script(TRACE_SCRIPT)
        script.on('message', on_message)
        script.load()

        # Initialize tracing
        script.exports_sync.init(use_stalker)

        print("[Frida] Tracing active. Press Ctrl+C to stop and report...")

        try:
            while True:
                time.sleep(1)
        except KeyboardInterrupt:
            print("\n[Frida] Stopping...")

        # Get final report
        script.exports_sync.report()
        time.sleep(0.5)  # Wait for message

        session.detach()

        return coverage_data

    except frida.ProcessNotFoundError:
        print(f"[ERROR] Process {pid} not found")
        return None
    except Exception as e:
        print(f"[ERROR] {e}")
        return None


def spawn_and_trace(args: list, use_stalker: bool = False):
    """Spawn Wine process and trace from start."""
    print(f"\n[Frida] Spawning: wine {' '.join(args)}")

    # Build wine command
    wine_path = "/opt/homebrew/bin/wine64"
    if not os.path.exists(wine_path):
        wine_path = "wine64"

    coverage_data = None

    def on_message(message, data):
        nonlocal coverage_data
        if message['type'] == 'send':
            payload = message['payload']
            if payload.get('type') == 'coverage':
                coverage_data = payload['data']
            else:
                print(f"[MSG] {payload}")
        elif message['type'] == 'error':
            print(f"[ERROR] {message.get('stack', message)}")

    try:
        # Spawn with Frida
        pid = frida.spawn([wine_path] + args)
        print(f"[Frida] Spawned PID {pid}")

        session = frida.attach(pid)
        script = session.create_script(TRACE_SCRIPT)
        script.on('message', on_message)
        script.load()

        # Initialize and resume
        script.exports_sync.init(use_stalker)
        frida.resume(pid)

        print("[Frida] Process running. Waiting for completion...")

        # Wait for process to finish (with timeout)
        try:
            for _ in range(120):  # 2 minute timeout
                time.sleep(1)
                # Check if process still exists
                try:
                    os.kill(pid, 0)
                except OSError:
                    print("[Frida] Process exited")
                    break
        except KeyboardInterrupt:
            print("\n[Frida] Interrupted")

        # Get report
        try:
            script.exports_sync.report()
            time.sleep(0.5)
        except:
            pass

        session.detach()
        return coverage_data

    except Exception as e:
        print(f"[ERROR] {e}")
        import traceback
        traceback.print_exc()
        return None


def save_coverage_report(coverage_data: dict, output_path: str):
    """Save coverage report as JSON."""
    if not coverage_data:
        print("[WARN] No coverage data to save")
        return

    report = {
        "tool": "frida",
        "timestamp": datetime.now().isoformat(),
        "platform": "macos-wine",
        "functions_called": coverage_data.get("functions_called", {}),
        "total_calls": coverage_data.get("call_count", 0),
        "basic_blocks": len(coverage_data.get("basic_blocks", [])),
        "unique_functions": len(coverage_data.get("functions_called", {}))
    }

    with open(output_path, 'w') as f:
        json.dump(report, f, indent=2)

    print(f"\n[OK] Coverage saved to {output_path}")


def main():
    parser = argparse.ArgumentParser(description="Frida coverage tracer for target.dll")
    parser.add_argument("--attach", action="store_true", help="Attach to running Wine process")
    parser.add_argument("--spawn", nargs="+", help="Spawn and trace: exe [args...]")
    parser.add_argument("--list-exports", type=str, help="List exports from DLL")
    parser.add_argument("--pid", type=int, help="Specific PID to attach to")
    parser.add_argument("--stalker", action="store_true", help="Enable Stalker for BB coverage (slower)")
    parser.add_argument("--output", type=str, default="frida_coverage.json", help="Output JSON file")

    args = parser.parse_args()

    if args.list_exports:
        list_exports(args.list_exports)
        return

    if args.attach:
        pid = args.pid or find_wine_pid()
        if not pid:
            print("[ERROR] No Wine process found. Start one first or use --pid")
            sys.exit(1)
        coverage = attach_and_trace(pid, args.stalker)
        save_coverage_report(coverage, args.output)

    elif args.spawn:
        coverage = spawn_and_trace(args.spawn, args.stalker)
        save_coverage_report(coverage, args.output)

    else:
        parser.print_help()
        print("\nExamples:")
        print("  # Attach to running Wine:")
        print("  python3 frida_coverage.py --attach")
        print("")
        print("  # Spawn and trace:")
        print("  python3 frida_coverage.py --spawn coverage_test_driver.exe ../dlls/original/target.dll test_files")


if __name__ == "__main__":
    main()
