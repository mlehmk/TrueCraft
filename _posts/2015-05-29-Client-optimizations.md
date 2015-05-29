---
# vim: tw=80
layout: post
title: Client Optimizations
blurb: |
    I spent a little bit of time working on optimizing the client, with two major
    changes.
---

![](https://a.pomf.se/yquumh.png)

I spent a little bit of time working on optimizing the client, with two major
changes. First, I fixed [a bug](https://github.com/SirCmpwn/TrueCraft/issues/99)
reported by UnknownShadow200 - apparently the earlier changes to chunk rendering
made it so that blocks underground (and out of sight) were being rendered. After
the change, the vertex count of each chunk went down dramatically.

That change improves the everyday FPS, which for me is now stable at a
consistent 60 FPS (that's our target, by the way). The next change improves the
time it takes to render chunks to meshes, and this is visible in how quickly the
world appears around you after login and as you move. The code was refactored
until it could run in several threads, and the task was split up across
available cores. There are still optimiaztions to be made in each thread, but
that change is an important one regardless and definitely makes the game more
playable *now*.

That's all for now, no exciting new features to talk about today.
