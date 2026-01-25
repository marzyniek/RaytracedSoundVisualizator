# Diegetic sound visualization for hard-of-hearing players

**Author:** Martin Nastoupil

**Keywords:** Audio-visualization Gaming, Deaf or Hard of Hearing

	Visualizing in-game sound directly in the environment for Unity. Creating 	
	visual cues to convey sound information for hard-of-hearing players by 
	simulating the sound propagation through the virtual space using rays.

## 1. Problem and context

Audio in video games is not just an immersion feature that complements the visuals. In genres like First-Person Shooters (FPS), sound also conveys vital spatial information, like footsteps around a corner or distant gunfire that tells you exactly where an enemy is.

For players who are Deaf or Hard of Hearing (DHH), missing these cues creates a substantial accessibility barrier. This isn't just a niche issue either. According to the [World Health Organization](https://www.who.int/news-room/fact-sheets/detail/deafness-and-hearing-loss), over 430 million people globally suffer from disabling hearing loss and according to their estimates, this number is likely to grow.
While industry standards like subtitles are great for dialogue, they fail to communicate spatial data quickly enough for real-time reactions. You can read _what_ someone said, but reading _which_ sound came from _where_ takes too long in a fast-paced game.
### Existing solutions
Currently, the most common solution for this issue are UI based systems like one used in _Fortnite_. It uses a 2D overlayed ring around the player to show icons for footsteps, gunfire or loot. It works well, but it forces the player to look at a flat interface rather than the game world.
![Fortnite's Visualize Sound](https://www.charlieintel.com/cdn-image/wp-content/uploads/2023/02/fortnite-visualize-sound-effects-setting-1024x576.jpg?width=1200&quality=75&format=auto)
  

I wanted to challenge that approach. Instead of an abstract UI layer, I wanted to move the visualization directly into the 3D environment making the sound visible, immersive, and part of the world itself.
## 2. Project objectives
The goal of this project was to design a prototype that displays the in-game audio directly in the environment, rather than relying on 2D HUD overlays.

Specific objectives:

- **Sound propagation:** To implement a ‘physically-based’ simulation where visual cues appear on spots where the sound wave would likely hit geometry.

- **In-world Integration:** To integrate cues directly into the game environment to maintain immersion.

- **Performance:** To create a solution that is easily integrable into other projects and does not cause any major performance issues.

## 3. Process

I began with an analysis of existing ray-traced audio solutions, specifically drawing inspiration from the work of [Vericidum Audio](https://vercidium.com/audio). However, replicating a full-scale game engine with ray-traced audio was out of scope for this project. Consequently, the objective shifted toward creating a lightweight, visually focused solution that could be easily integrated into existing Unity projects.

Initially, I created a naive prototype that simulated sound propagation by casting rays originating from each active sound source. Any ray intersecting the listener within a defined radius triggered a visualization. While this proved the feasibility of the concept, it introduced significant performance bottlenecks. As the number of sound sources increased, the computational cost of simulating hundreds of rays per source became too much.

Therefore, the 'obvious' improvement was to mirror standard graphical raytracing techniques, so that the origin of the simulation was moved to the listener. This modification enabled a fixed budget of rays, regardless of the number of sound sources, thereby significantly stabilizing performance. Furthermore, centering the logic on the listener improved the system's modularity, making it easier to drop into different game scenes without complex dependencies.

After implementing the listener-centric approach, I soon realized that the outcome looked very different from the first one. The dots appeared more scattered, and overall usability decreased when compared to the initial approach. I attempted to resolve this issue by biasing the rays in the direction of the sound sources to increase the likelihood of the rays connecting with the source. And while it improved the overall look, it still was suboptimal.

Ultimately, I decided to go back to the source-centric approach. I applied the knowledge I gained throughout the whole process to simulate the rays much more effectively, utilizing Unity's job system for multi-threading. This change alone significantly increased the overall performance, and with some tweaks to the shader code, I managed to achieve even better performance than with the listener-centric approach.

## 4. Result

The final result is a Unity plugin that renders a visualization for real-time audio propagation. It maps sound directly onto the environment's geometry, allowing players to see sound bouncing through doorways or around obstacles.
![[showcase1.png]]
![[showcase2.png]]
![[showcase3.png]]
### 4.1 How it works

1. **Source Registry:** First, retrieve the position of all active sound sources that are within the listening distance.
2. **Ray casting:** There's a fixed budget of rays that are allowed per iteration, and these are evenly distributed between all active sound sources. Go through the list of retrieved sound sources, and for each of these, cast some number of rays in a random direction. The rays bounce around the scene and register each collision point they encounter up to a predetermined maximum bounce limit.
3. **Visualization:** Visual indicators are rendered at all collision points where the energy of the ray is larger than a predetermined parameter.
	- **Size:** The size of the indicator is determined by the number of bounces and the distance the ray traveled from the sound source.
	- **Opacity:** Opacity is decided by the current lifetime of the indicator and the distance from the listener.
	- **Color:** The indicator's color is determined by each sound source in a complementary component (e.g., distinct colors for footsteps vs. gunfire).

### 4.2 Setup

The plugin is designed as a simple "drop-in" tool for developers.

- Import the Unity .package file by drag-and-drop to your project's assets.
- After importing the package, a **Tools** section should appear in the top toolbar menu. Use the custom _Setup Audio Sources_ script to automatically attach tracking components to all existing audio sources.
- Attach the imported __RaytracedAudioVisualizer__ prefab onto the listener.

![[install.png]]
### 4.3 Settings
The script attached to the **RaytracedAudioVisualizer** prefab allows you to modify how the visualizations look and behave. 

The available settings are:
- **Obstacle Layer** - Defines which layers the rays interact with. Default set to "Everything," but assigning a specific layer for static geometry (obstacles) is recommended.
- **Scan Radius** - The maximum distance from the listener within which sound sources are tracked and visualized.
- **Ray Count** - The total number of rays cast per frame.
- **Max bounces** - Maximum amount of bounces each ray performs.
- **Bounce Energy Loss Multiplier** - A multiplier applied to a ray's energy after every bounce.
- **Dot Mesh** - The mesh used for the visual indicator (default set to Quad).
- **Dot Material** - The material used to render the visual cues. (By default uses custom shader included in the package)
- **Max Dot Capacity** - The maximum number of visual indicators that can be rendered simultaneously.
- **Show Debug Gizmos** - Toggles debug lines in the Scene View to visualize the path of the rays.
![[settings.png]]
## 5. Limitations and future extensions

While the prototype functions well as a proof of concept, there are several areas I would like to improve in the future.

- **Visuals:** The overall look of the visualization in the current state is quite basic, as the visual clues are simply a collection of colored quads. Therefore, using this visualization in a game with a more realistic environment could make the visuals stand out quite noticeably and break the overall aesthetics.

- **Physics Approximation:** Using rays to simulate sound is an efficient approximation, but it is not really perfect. Since sound travels in waves, it diffracts and bends in ways rays do not. Additionally, the current system does not calculate sound transmission through objects. Even a thin sheet of paper currently blocks the visualization entirely.

- **User Validation:** The most critical next step is user testing. I have not yet conducted studies with any players. Without this data, it is difficult to determine if these diegetic cues improve gameplay or if they simply add visual clutter and are more distracting than helpful.
## 6. Conclusion

This tool is currently just a proof of concept, but it opens up the door for further exploration into diegetic accessibility tools. Given its ease of integration, I believe it offers an interesting alternative to the standard solutions currently used for DHH players.

For me personally, the development process served as a good way to learn with the Unity engine. I didn't have much prior experience, so I gained some insight into development workflows and performance optimization, specifically through multithreading and custom shaders.

Moving forward, I plan to expand this project into my Bachelor's thesis. In my next steps I will focus on refining the ray propagation logic and, most importantly, conducting a user study to determine the system's effectiveness with actual players.

## References

- Coutinho, F., Prates, R. O., & Chaimowicz, L. (2011). An Analysis of Information Conveyed through Audio in an FPS Game and Its Impact on Deaf Players' Experience. _2011 Brazilian Symposium on Games and Digital Entertainment_, 53-62. doi:10.1109/SBGAMES.2011.16

- Li, Z., Connell, S., Dannels, W., & Peiris, R. (2022). SoundVizVR: Sound Indicators for Accessible Sounds in Virtual Reality for Deaf or Hard-of-Hearing Users. _Proceedings of the 24th International ACM SIGACCESS Conference on Computers and Accessibility_. doi:10.1145/3517428.3544817

- World Health Organization. (2025). Deafness and Hearing Loss. WHO Fact Sheet. Retrieved from [https://www.who.int/news-room/fact-sheets/detail/deafness-and-hearing-loss](https://www.who.int/news-room/fact-sheets/detail/deafness-and-hearing-loss)

- 2025 Accessibility Labs, LLC. (2025). Feature Highlight: Fortnite’s Sound Visualizer. Accessibility Labs: Case Studies. Retrieved from [https://accessibility-labs.com/feature-highlight-fortnites-sound-visualizer/](https://accessibility-labs.com/feature-highlight-fortnites-sound-visualizer/)

- Vercidium. (2025). Vercidium Audio. Retrieved from [https://vercidium.com/audio](https://vercidium.com/audio)
