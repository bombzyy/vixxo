/**
 * Vixxo 3D Interactive WebGL Engine
 * Built with Three.js for ultra-smooth 60+ FPS scroll-driven 3D rendering.
 */

(function () {
  const canvas = document.getElementById('webgl-canvas');
  if (!canvas || typeof THREE === 'undefined') return;

  // Scene setup
  const scene = new THREE.Scene();
  scene.fog = new THREE.FogExp2(0x07090e, 0.0018);

  const camera = new THREE.PerspectiveCamera(55, window.innerWidth / window.innerHeight, 0.1, 1000);
  camera.position.set(0, 0, 32);

  const renderer = new THREE.WebGLRenderer({
    canvas: canvas,
    alpha: true,
    antialias: true,
    powerPreference: 'high-performance'
  });
  renderer.setSize(window.innerWidth, window.innerHeight);
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));

  // Lighting
  const ambientLight = new THREE.AmbientLight(0x1e293b, 1.2);
  scene.add(ambientLight);

  const pointLight1 = new THREE.PointLight(0x38bdf8, 3.5, 60);
  pointLight1.position.set(12, 14, 15);
  scene.add(pointLight1);

  const pointLight2 = new THREE.PointLight(0x818cf8, 2.8, 60);
  pointLight2.position.set(-14, -10, 10);
  scene.add(pointLight2);

  const coreGroup = new THREE.Group();
  scene.add(coreGroup);

  // 1. Microphone Capsule 3D Model
  // Outer Grill Cage (Icosahedron Wireframe)
  const cageGeo = new THREE.IcosahedronGeometry(4.2, 2);
  const cageMat = new THREE.MeshStandardMaterial({
    color: 0x38bdf8,
    wireframe: true,
    transparent: true,
    opacity: 0.45,
    roughness: 0.2,
    metalness: 0.9
  });
  const cageMesh = new THREE.Mesh(cageGeo, cageMat);
  coreGroup.add(cageMesh);

  // Inner Glowing Core (Diaphragm)
  const coreGeo = new THREE.SphereGeometry(2.4, 32, 32);
  const coreMat = new THREE.MeshStandardMaterial({
    color: 0x0284c7,
    emissive: 0x0ea5e9,
    emissiveIntensity: 0.6,
    roughness: 0.1,
    metalness: 0.8
  });
  const coreMesh = new THREE.Mesh(coreGeo, coreMat);
  coreGroup.add(coreMesh);

  // Concentric Gyro Soundfield Rings
  const ringMaterials = [
    new THREE.MeshBasicMaterial({ color: 0x38bdf8, transparent: true, opacity: 0.7, wireframe: true }),
    new THREE.MeshBasicMaterial({ color: 0x818cf8, transparent: true, opacity: 0.6, wireframe: true }),
    new THREE.MeshBasicMaterial({ color: 0xc084fc, transparent: true, opacity: 0.5, wireframe: true })
  ];

  const rings = [];
  const ringGeos = [
    new THREE.TorusGeometry(5.8, 0.06, 16, 80),
    new THREE.TorusGeometry(7.2, 0.05, 16, 90),
    new THREE.TorusGeometry(8.6, 0.04, 16, 100)
  ];

  ringGeos.forEach((geo, i) => {
    const ring = new THREE.Mesh(geo, ringMaterials[i]);
    rings.push(ring);
    coreGroup.add(ring);
  });

  // Mic Body Base (Sleek Matte Obsidian Cylinder)
  const bodyGeo = new THREE.CylinderGeometry(2.0, 1.6, 6.5, 32);
  const bodyMat = new THREE.MeshStandardMaterial({
    color: 0x0f172a,
    roughness: 0.3,
    metalness: 0.8
  });
  const bodyMesh = new THREE.Mesh(bodyGeo, bodyMat);
  bodyMesh.position.y = -5.8;
  coreGroup.add(bodyMesh);

  // Accent ring around body
  const ringAccentGeo = new THREE.TorusGeometry(2.05, 0.08, 16, 64);
  const ringAccentMat = new THREE.MeshBasicMaterial({ color: 0x38bdf8 });
  const ringAccent = new THREE.Mesh(ringAccentGeo, ringAccentMat);
  ringAccent.rotation.x = Math.PI / 2;
  ringAccent.position.y = -3.2;
  coreGroup.add(ringAccent);

  // 2. Cosmic Audio Particle Field (1,200 particles)
  const particleCount = 1400;
  const particleGeo = new THREE.BufferGeometry();
  const positions = new Float32Array(particleCount * 3);
  const colors = new Float32Array(particleCount * 3);

  const colorPalette = [
    new THREE.Color(0x38bdf8), // Cyan
    new THREE.Color(0x60a5fa), // Blue
    new THREE.Color(0xa78bfa), // Violet
    new THREE.Color(0x34d399)  // Emerald
  ];

  for (let i = 0; i < particleCount; i++) {
    const r = 10 + Math.random() * 45;
    const theta = Math.random() * Math.PI * 2;
    const phi = Math.acos((Math.random() * 2) - 1);

    positions[i * 3] = r * Math.sin(phi) * Math.cos(theta);
    positions[i * 3 + 1] = r * Math.sin(phi) * Math.sin(theta);
    positions[i * 3 + 2] = r * Math.cos(phi);

    const c = colorPalette[Math.floor(Math.random() * colorPalette.length)];
    colors[i * 3] = c.r;
    colors[i * 3 + 1] = c.g;
    colors[i * 3 + 2] = c.b;
  }

  particleGeo.setAttribute('position', new THREE.BufferAttribute(positions, 3));
  particleGeo.setAttribute('color', new THREE.BufferAttribute(colors, 3));

  const particleMat = new THREE.PointsMaterial({
    size: 0.18,
    vertexColors: true,
    transparent: true,
    opacity: 0.75,
    blending: THREE.AdditiveBlending
  });

  const particleField = new THREE.Points(particleGeo, particleMat);
  scene.add(particleField);

  // Scroll & Mouse Interaction State
  let targetRotationX = 0;
  let targetRotationY = 0;
  let targetRotationZ = 0;
  let targetPositionX = 0;
  let targetPositionY = 0;
  let targetScale = 1;
  let scrollProgress = 0;
  let mouseX = 0;
  let mouseY = 0;
  let livePulse = 0;

  // Window resize handler
  window.addEventListener('resize', () => {
    camera.aspect = window.innerWidth / window.innerHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(window.innerWidth, window.innerHeight);
  });

  // Mouse move handler (Parallax Tilt)
  window.addEventListener('mousemove', (e) => {
    mouseX = (e.clientX / window.innerWidth) * 2 - 1;
    mouseY = -(e.clientY / window.innerHeight) * 2 + 1;
  });

  // Scroll handler (Timeline Mapping)
  window.addEventListener('scroll', () => {
    const maxScroll = document.documentElement.scrollHeight - window.innerHeight;
    scrollProgress = maxScroll > 0 ? window.scrollY / maxScroll : 0;

    // Responsive 3D layout based on scroll
    targetRotationY = scrollProgress * Math.PI * 5;
    targetRotationX = Math.sin(scrollProgress * Math.PI * 3) * 0.5;
    targetRotationZ = Math.cos(scrollProgress * Math.PI * 2) * 0.3;

    // Float across viewport
    if (scrollProgress < 0.25) {
      // Hero: centered, prominent
      targetPositionX = 6 * (1 - scrollProgress * 4);
      targetPositionY = -1;
      targetScale = 1.15;
    } else if (scrollProgress < 0.55) {
      // Pipeline / Features: moved to right to frame text
      targetPositionX = window.innerWidth > 900 ? 11 : 0;
      targetPositionY = 1;
      targetScale = 0.95;
    } else if (scrollProgress < 0.8) {
      // DSP Lab: centered floating core
      targetPositionX = window.innerWidth > 900 ? -11 : 0;
      targetPositionY = -0.5;
      targetScale = 1.05;
    } else {
      // CTA / Footer: glowing aura
      targetPositionX = 0;
      targetPositionY = 0;
      targetScale = 1.25;
    }
  });

  // Trigger initial scroll calculation
  window.dispatchEvent(new Event('scroll'));

  // Global Audio Pulse API (Triggered by Audio Simulator)
  window.Vixxo3D = {
    triggerPulse: function (intensity) {
      livePulse = Math.min(1.5, Math.max(livePulse, intensity || 0.8));
    }
  };

  // Main Render Loop (60 FPS)
  const clock = new THREE.Clock();

  function animate() {
    requestAnimationFrame(animate);

    const delta = clock.getDelta();
    const elapsedTime = clock.getElapsedTime();

    // Smooth lerping for position & rotation
    coreGroup.rotation.y += (targetRotationY + mouseX * 0.4 - coreGroup.rotation.y) * 0.08;
    coreGroup.rotation.x += (targetRotationX - mouseY * 0.3 - coreGroup.rotation.x) * 0.08;
    coreGroup.rotation.z += (targetRotationZ - coreGroup.rotation.z) * 0.08;

    coreGroup.position.x += (targetPositionX - coreGroup.position.x) * 0.06;
    coreGroup.position.y += (targetPositionY + Math.sin(elapsedTime * 1.6) * 0.4 - coreGroup.position.y) * 0.06;

    // Spin Gyro Rings independently
    rings[0].rotation.x = elapsedTime * 0.45;
    rings[0].rotation.y = elapsedTime * 0.3;
    rings[1].rotation.y = -elapsedTime * 0.35;
    rings[1].rotation.z = elapsedTime * 0.25;
    rings[2].rotation.z = elapsedTime * 0.2;
    rings[2].rotation.x = -elapsedTime * 0.4;

    // Slowly drift particles
    particleField.rotation.y = elapsedTime * 0.04;
    particleField.rotation.x = elapsedTime * 0.02;

    // React to live audio pulse
    if (livePulse > 0.01) {
      const pulseScale = targetScale * (1 + livePulse * 0.25);
      coreGroup.scale.set(pulseScale, pulseScale, pulseScale);
      coreMat.emissiveIntensity = 0.6 + livePulse * 1.5;
      cageMat.opacity = 0.45 + livePulse * 0.4;
      pointLight1.intensity = 3.5 + livePulse * 4.0;
      livePulse -= delta * 1.8;
    } else {
      coreGroup.scale.lerp(new THREE.Vector3(targetScale, targetScale, targetScale), 0.08);
      coreMat.emissiveIntensity = 0.6;
      cageMat.opacity = 0.45;
      pointLight1.intensity = 3.5;
    }

    renderer.render(scene, camera);
  }

  animate();
})();
