"""
Authors the chibi's second jump (Animator state JumpDouble, clip role `jumpDouble`) on the Mixamo
X Bot skeleton and writes `Straight Jump.fbx`: a rise with the arms down and the legs straight and
together. None of the clips in the project or the Mixamo inbox has that pose in the air.

    "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --factory-startup \
        --python Tools/Blender/chibi_straight_jump.py -- \
        "Assets/_Project/Animation/Chibi/Run To Dive.fbx" \
        "Assets/_Project/Animation/Chibi/Run To Dive.fbx.meta" \
        "Assets/_Project/Animation/Chibi/Straight Jump.fbx"

The rig code and the pose maths are in chibi_backflip.py, including what each pose value means.

Timing: JumpDouble plays at speed 1 and hands to Fall as the jump starts to come down, about 0.65 s
from a sprint. So the clip lasts 0.70 s and keeps moving slightly the whole way, rather than
freezing on one pose. Its last frames open the arms and soften the knees toward Fall.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import chibi_backflip as rig  # noqa: E402

LAST_FRAME = 21

POSES = [
    # Leaving the ground: the push still finishing, arms pressing down and slightly back.
    (0, dict(spine=(3, 3, 2), neck=0, head=-2, clav=-2,
             arm=(0.30, -0.90, -0.32), twist=0, elbow=20, r_elbow=20,
             thigh=(0.02, -0.98, 0.18), knee_out=2, knee=24, foot=26, toe=0, curl=24)),
    # Fully extended: straight legs together, toes pointed, arms straight down, chest up.
    (4, dict(spine=(-3, -3, -3), neck=-2, head=-6, clav=-4,
             arm=(0.20, -0.97, -0.12), twist=0, elbow=6,
             thigh=(-0.03, -1.0, 0.0), knee_out=0, knee=2, foot=46, toe=-6, curl=30)),
    # The rise: the same line, drifting a little so that it never reads as a frozen frame.
    (12, dict(spine=(-4, -4, -3), neck=-2, head=-8, clav=-3,
              arm=(0.25, -0.96, -0.06), twist=0, elbow=9,
              thigh=(-0.03, -1.0, 0.01), knee_out=0, knee=3, foot=50, toe=-8, curl=30)),
    (17, dict(spine=(-3, -3, -2), neck=-1, head=-7, clav=-1,
              arm=(0.32, -0.94, -0.02), twist=0, elbow=12,
              thigh=(-0.02, -1.0, 0.03), knee_out=0, knee=5, foot=48, toe=-6, curl=26)),
    # Near the top: arms start to float out and the knees soften, on the way to Fall's pose.
    (21, dict(spine=(0, 0, 0), neck=0, head=-4, clav=2,
              arm=(0.46, -0.88, 0.04), twist=0, elbow=18,
              thigh=(0.0, -0.99, 0.10), knee_out=2, knee=14, foot=38, toe=0, curl=20)),
]

rig.POSES = POSES
rig.LAST_FRAME = LAST_FRAME
rig.FLIP_KEYS = [(0, 0.0), (LAST_FRAME, 0.0)]   # stays upright: nothing here rotates the hips

rig.main()
