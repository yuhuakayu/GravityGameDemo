# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a Unity 2D game demo (`GravityGameDemo`) built around a world-rotation gravity mechanic — instead of changing the player's gravity direction, the entire world rotates around a pivot point, creating the illusion of shifting gravity.

## Build & Run

Open the project in Unity Editor (recommended Unity 2022 LTS+). There is no CLI build script; use Unity's Build Settings (File → Build Settings) or the Editor Play button.

## Architecture

All scripts live in `Assets/Resource/Scripts/` under the namespace `Resource.Scripts`. The project uses Unity's **new Input System** (`UnityEngine.InputSystem`) — do not use the legacy `Input` class.

### Scripts

- **PlayerController.cs** — Attaches to the player GameObject. Handles horizontal movement via `Rigidbody2D.linearVelocity` and jumping. Ground detection uses `OnCollisionEnter2D`/`OnCollisionExit2D` with contact normal threshold (`normal.y > 0.5f`). Dual input: keyboard (A/D/Space) and gamepad (L2/R2 triggers for movement, South button/× for jump).

- **WorldRoot.cs** (class: `WorldRotator`) — Attaches to the world root GameObject. Rotates the entire scene around an optional `pivot` Transform using `Transform.RotateAround`. Without a pivot, rotates in place. Smoothed via `Mathf.LerpAngle`. Input: keyboard (Q/E) and gamepad right stick X-axis.

- **DS5Test.cs** — Debug-only script. Polls and logs gamepad axes every 0.5s. Notes that Steam Input maps DS5 gyroscope data to the right stick axis.

### Input Conventions

| Action | Keyboard | Gamepad |
|---|---|---|
| Move left/right | A / D | L2 / R2 triggers |
| Jump | Space | South button (×) |
| Rotate world CW/CCW | E / Q | Right stick X |

Gamepad input uses `Gamepad.current`; always null-check before reading values. The project is tested with DualSense (DS5) via Steam Input.

### Key Design Notes

- The gravity effect comes from **rotating the world**, not from modifying `Physics2D.gravity` or the player's orientation. Keep this in mind when adding new physics objects — they should be children of the world root so they rotate with it.
- Dead zones for triggers are applied manually (`< 0.05f → 0`), not via Input System's built-in processor.
