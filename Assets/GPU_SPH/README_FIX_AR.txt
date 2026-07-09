إصلاح مشكلة ظهور الجزيئات كنقاط زهر صغيرة/غبار بدل تيار نازل:

استبدلي الملفات الثلاثة الموجودة بالمشروع بهذه النسخ بنفس الأسماء:
- BucketParticleSystemCustom.cs
- BucketParticleSystemCustomGPU.compute
- GPUSPHParticleInstanced.shader

ما الذي تغير؟
1) صار كل Particle يبدأ Inactive بدل ما يظهر مباشرة داخل المشهد.
2) الـ Compute Shader صار يتجاهل الجزيئات غير المفعلة في Grid / Density / Integration.
3) الـ Shader صار لا يرسم الجزيئات غير المفعلة.
4) الجزيئات التي تنزل تحت حدود المحاكاة تُخفى بدل ما تتجمع كنقاط.
5) كبرت Render Particle Size الافتراضية قليلاً من 0.012 إلى 0.020 حتى يبين التيار أوضح.

بعد الاستبدال:
- شغلي Play.
- إذا كان Pause Simulation مفعّل، أطفئيه.
- إذا غيرتي عدد الجزيئات، فعّلي Apply Particle Count Now مرة واحدة.

إعدادات مقترحة:
Render Particle Size = 0.018 - 0.024
Outlet Shape = Circle
Outlet Shape Radius = 0.025 - 0.04
Random Outlet Jitter = 0.05 - 0.15
Spread Angle = 0.5 - 1.5
Emission Rate = 12000 - 25000
Max Emitted Per Fixed Step = 512 - 1024
