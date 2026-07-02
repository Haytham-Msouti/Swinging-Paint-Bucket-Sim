نسخة Safe Edit من BucketParticleSystemCustom

المشكلة السابقة:
- السكربت كان يشغل GPU SPH مباشرة على 250000 particle.
- وأي تغيير في Max Particles أثناء Play كان يعيد بناء buffers فوراً، فيعلق Unity.
- Max SPH Particles كان مربوطاً إجبارياً مع Max Particles لذلك لا يمكن تعديله وحده.

الحل في هذه النسخة:
1) نفس اسم الكلاس بقي: BucketParticleSystemCustom.
2) السكربت يبدأ Pause Simulation = true حتى تقدري تعدلي القيم بدون تعليق.
3) تغيير كمية الجزيئات لا يعيد بناء buffers فوراً.
4) بعد تغيير العدد، فعّلي Apply Particle Count Now مرة واحدة لإعادة البناء بأمان.
5) إذا تريدين Max SPH Particles يتعدل وحده، أطفئي Sync Max SPH Particles With Max Particles.

طريقة الاستخدام:
- ضعي BucketParticleSystemCustom.cs بدل القديم.
- ضعي BucketParticleSystemCustomGPU.compute معه.
- ضعي GPUSPHParticleInstanced.shader معه.
- Material الجزيئات لازم يكون Shader = Custom/GPUSPHParticleInstanced.
- بالInspector عبي:
  Sph Compute = BucketParticleSystemCustomGPU.compute
  Particle Material = material الجديد

خطوات تعديل الكمية بدون تعليق:
1) خليه Pause Simulation = true.
2) غيري Max Particles إلى 250000 أو الرقم الذي تريدينه.
3) خلي Sync Max SPH Particles With Max Particles = true إذا بدك كل الجزيئات SPH.
4) فعّلي Apply Particle Count Now مرة واحدة.
5) انتظري رسالة Console: rebuilt safely.
6) أطفئي Pause Simulation للتشغيل.

إعداد آمن لـ 250 ألف:
Max Particles = 250000
Max SPH Particles = 250000
Grid Resolution = 48
Max Particles Per Cell = 32
Simulation Steps Per Frame = 1
Fixed Dt Override = 0.0035
Render Particle Size = 0.012

إذا علّق:
- ارجعي Pause Simulation = true.
- خفضي Max Particles Per Cell إلى 16 أو 24.
- أو خفضي Grid Resolution إلى 40.
- لا تغيري العدد أثناء التشغيل إلا وPause Simulation مفعلة.
