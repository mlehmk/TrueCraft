---
# vim: tw=80
title: Complex block models
layout: post
blurb: |
    More complex block models are being built out in the renderer, including
    torches.
---

![](https://a.pomf.se/abslrj.png)

More complex block models are being built out in the renderer, including
torches. This one is pretty simple - it starts with a scaled version of the
usual cube that most blocks use, adds special texture mappings, and skews the
model depending on the orientation of the torch. More complex blocks like beds
and cacti and so on should also be possible in the near future.

Next, however, I'm going to spend some time refactoring what we already have now
that I have a better understanding of how the client should take shape. Then,
I'll be adding a particle system so the torches can look prettier. At that point
I'll probably go back to working on the server for a while.
