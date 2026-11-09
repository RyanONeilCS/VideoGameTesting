"""
Quick look at a play session recorded by TelemetryLogger.

Usage (from the StalkerPrototype folder):
    pip install pandas matplotlib
    python Tools/analyze_telemetry.py                       # newest session
    python Tools/analyze_telemetry.py Telemetry/session_XXXX.csv
"""
import glob
import os
import sys

import matplotlib.pyplot as plt
import pandas as pd

here = os.path.dirname(os.path.abspath(__file__))
if len(sys.argv) > 1:
    path = sys.argv[1]
else:
    sessions = glob.glob(os.path.join(here, "..", "Telemetry", "session_*.csv"))
    if not sessions:
        sys.exit("No sessions found yet. Press Play in Unity first!")
    path = max(sessions, key=os.path.getmtime)

df = pd.read_csv(path)
print(f"Loaded {path}: {len(df)} rows, {df['time'].max():.0f} seconds\n")

print("Share of time in each state:")
print(df.groupby("monster")["state"].value_counts(normalize=True).round(2), "\n")

print("How often the player saw the monster, per state:")
print(df.groupby("state")["player_sees_monster"].mean().round(2), "\n")

fig, axes = plt.subplots(1, 3, figsize=(18, 5))

# 1) Distance over time, coloured by state
for state, g in df.groupby("state"):
    axes[0].scatter(g["time"], g["distance"], s=5, label=state)
axes[0].set(title="Monster distance over time", xlabel="time (s)", ylabel="distance (m)")
axes[0].legend()

# 2) Learning: boldness over time
for monster, g in df.groupby("monster"):
    axes[1].plot(g["time"], g["boldness"], label=monster)
axes[1].set(title="Learned boldness (stays inside its limits)", xlabel="time (s)", ylabel="boldness")
axes[1].legend()

# 3) Where the player spent time (heat map)
axes[2].hist2d(df["player_x"], df["player_z"], bins=40, cmap="magma")
axes[2].set(title="Player position heat map", xlabel="x", ylabel="z")
axes[2].set_aspect("equal")

plt.tight_layout()
plt.show()
