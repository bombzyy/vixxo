/**
 * Vixxo Live DSP Audio Simulator
 * Interactive Web Audio API test lab with real-time waveform & filter toggles.
 */

(function () {
  let audioCtx = null;
  let isPlaying = false;
  let oscillator = null;
  let noiseNode = null;
  let gainNode = null;
  let biquadFilter = null;
  let analyser = null;
  let animationFrameId = null;

  // DSP States
  let stateHpf = true;
  let stateGate = true;
  let stateLimiter = true;
  let currentGainDb = 12.0;

  // DOM Elements
  const startBtn = document.getElementById('btn-test-audio');
  const toggleHpf = document.getElementById('toggle-hpf');
  const toggleGate = document.getElementById('toggle-gate');
  const toggleLimiter = document.getElementById('toggle-limiter');
  const gainSlider = document.getElementById('slider-gain');
  const gainValueDisplay = document.getElementById('gain-val-display');
  const canvas = document.getElementById('oscilloscope');
  const meterPeakFill = document.getElementById('meter-peak-fill');
  const meterRmsFill = document.getElementById('meter-rms-fill');
  const peakDbText = document.getElementById('peak-db-text');
  const rmsDbText = document.getElementById('rms-db-text');

  if (!canvas) return;
  const ctx = canvas.getContext('2d');

  function initAudio() {
    if (audioCtx) return;
    const AudioContext = window.AudioContext || window.webkitAudioContext;
    audioCtx = new AudioContext();

    analyser = audioCtx.createAnalyser();
    analyser.fftSize = 2048;

    gainNode = audioCtx.createGain();
    gainNode.gain.value = Math.pow(10, currentGainDb / 20);

    biquadFilter = audioCtx.createBiquadFilter();
    biquadFilter.type = 'highpass';
    biquadFilter.frequency.value = stateHpf ? 80 : 10;
    biquadFilter.Q.value = 0.707;

    // Connect DSP chain
    biquadFilter.connect(gainNode);
    gainNode.connect(analyser);

    // Audio destination (low volume so it doesn't blast ears)
    const masterOut = audioCtx.createGain();
    masterOut.gain.value = 0.18;
    gainNode.connect(masterOut);
    masterOut.connect(audioCtx.destination);
  }

  function startSynthesizer() {
    initAudio();
    if (audioCtx.state === 'suspended') {
      audioCtx.resume();
    }

    // Voice Formant Simulator (Harmonic multi-oscillator voice drone)
    oscillator = audioCtx.createOscillator();
    oscillator.type = 'sawtooth';
    oscillator.frequency.value = 140; // Human vocal fundamental pitch

    // Low rumble frequency (simulates desk bump)
    const rumbleOsc = audioCtx.createOscillator();
    rumbleOsc.type = 'sine';
    rumbleOsc.frequency.value = 45; // 45 Hz room rumble

    const rumbleGain = audioCtx.createGain();
    rumbleGain.gain.value = stateHpf ? 0.0 : 0.8; // Cut by HPF

    // White noise generator for room hiss
    const bufferSize = audioCtx.sampleRate * 2;
    const noiseBuffer = audioCtx.createBuffer(1, bufferSize, audioCtx.sampleRate);
    const output = noiseBuffer.getChannelData(0);
    for (let i = 0; i < bufferSize; i++) {
      output[i] = Math.random() * 2 - 1;
    }

    noiseNode = audioCtx.createBufferSource();
    noiseNode.buffer = noiseBuffer;
    noiseNode.loop = true;

    const noiseGain = audioCtx.createGain();
    noiseGain.gain.value = stateGate ? 0.005 : 0.08; // Cut by Gate

    // Modulate pitch slightly for natural voice cadence
    const lfo = audioCtx.createOscillator();
    lfo.frequency.value = 4.5;
    const lfoGain = audioCtx.createGain();
    lfoGain.gain.value = 6;
    lfo.connect(oscillator.frequency);

    // Connect generators into filter
    oscillator.connect(biquadFilter);
    rumbleOsc.connect(rumbleGain);
    rumbleGain.connect(biquadFilter);
    noiseNode.connect(noiseGain);
    noiseGain.connect(biquadFilter);

    oscillator.start();
    rumbleOsc.start();
    noiseNode.start();
    lfo.start();

    isPlaying = true;
    startBtn.innerHTML = '<i data-lucide="square"></i> Stop Audio Test';
    if (window.lucide) window.lucide.createIcons();

    drawWaveform();
  }

  function stopSynthesizer() {
    if (audioCtx) {
      audioCtx.close();
      audioCtx = null;
    }
    isPlaying = false;
    cancelAnimationFrame(animationFrameId);

    if (startBtn) {
      startBtn.innerHTML = '<i data-lucide="play"></i> Test Studio Voice';
      if (window.lucide) window.lucide.createIcons();
    }

    // Reset meters & canvas
    if (meterPeakFill) meterPeakFill.style.width = '0%';
    if (meterRmsFill) meterRmsFill.style.width = '0%';
    if (peakDbText) peakDbText.textContent = '-inf dB';
    if (rmsDbText) rmsDbText.textContent = '-inf dB';

    ctx.clearRect(0, 0, canvas.width, canvas.height);
    drawIdleGrid();
  }

  function drawIdleGrid() {
    canvas.width = canvas.parentElement.clientWidth;
    canvas.height = 180;
    ctx.fillStyle = '#080c14';
    ctx.fillRect(0, 0, canvas.width, canvas.height);

    ctx.strokeStyle = 'rgba(56, 189, 248, 0.15)';
    ctx.lineWidth = 1;

    // Grid lines
    for (let x = 0; x < canvas.width; x += 40) {
      ctx.beginPath();
      ctx.moveTo(x, 0);
      ctx.lineTo(x, canvas.height);
      ctx.stroke();
    }
    for (let y = 0; y < canvas.height; y += 30) {
      ctx.beginPath();
      ctx.moveTo(0, y);
      ctx.lineTo(canvas.width, y);
      ctx.stroke();
    }

    // Center baseline
    ctx.strokeStyle = 'rgba(56, 189, 248, 0.4)';
    ctx.beginPath();
    ctx.moveTo(0, canvas.height / 2);
    ctx.lineTo(canvas.width, canvas.height / 2);
    ctx.stroke();

    ctx.fillStyle = 'rgba(148, 163, 184, 0.5)';
    ctx.font = '12px JetBrains Mono';
    ctx.fillText('48 kHz 16-bit PCM • IDLE (Press Test to Start)', 18, 28);
  }

  function drawWaveform() {
    if (!isPlaying || !analyser) return;

    animationFrameId = requestAnimationFrame(drawWaveform);
    canvas.width = canvas.parentElement.clientWidth;
    canvas.height = 180;

    const bufferLength = analyser.frequencyBinCount;
    const dataArray = new Uint8Array(bufferLength);
    analyser.getByteTimeDomainData(dataArray);

    ctx.fillStyle = '#080c14';
    ctx.fillRect(0, 0, canvas.width, canvas.height);

    // Subtle grid
    ctx.strokeStyle = 'rgba(255, 255, 255, 0.05)';
    for (let y = 0; y < canvas.height; y += 30) {
      ctx.beginPath();
      ctx.moveTo(0, y);
      ctx.lineTo(canvas.width, y);
      ctx.stroke();
    }

    // Oscilloscope path
    ctx.lineWidth = 2.5;
    ctx.strokeStyle = stateLimiter ? '#38bdf8' : '#ef4444';
    ctx.shadowBlur = 10;
    ctx.shadowColor = stateLimiter ? '#0284c7' : '#dc2626';

    ctx.beginPath();
    const sliceWidth = canvas.width / bufferLength;
    let x = 0;
    let peakVal = 0;
    let sumSq = 0;

    for (let i = 0; i < bufferLength; i++) {
      let v = dataArray[i] / 128.0; // 0 to 2, 1 = center
      let norm = v - 1.0;

      // Soft-Knee Limiter simulation
      if (stateLimiter) {
        if (norm > 0.8) norm = 0.8 + Math.tanh((norm - 0.8) * 0.5) * 0.15;
        if (norm < -0.8) norm = -0.8 - Math.tanh((-norm - 0.8) * 0.5) * 0.15;
      }

      const absV = Math.abs(norm);
      if (absV > peakVal) peakVal = absV;
      sumSq += norm * norm;

      const y = (norm * 0.75 + 0.5) * canvas.height;

      if (i === 0) {
        ctx.moveTo(x, y);
      } else {
        ctx.lineTo(x, y);
      }

      x += sliceWidth;
    }

    ctx.stroke();
    ctx.shadowBlur = 0;

    // Peak & RMS Math
    const rmsVal = Math.sqrt(sumSq / bufferLength);
    const peakDb = peakVal > 0.0001 ? 20 * Math.log10(peakVal) : -96;
    const rmsDb = rmsVal > 0.0001 ? 20 * Math.log10(rmsVal) : -96;

    // Update Meter UI
    const peakPct = Math.min(100, Math.max(0, (peakDb + 48) * (100 / 48)));
    const rmsPct = Math.min(100, Math.max(0, (rmsDb + 48) * (100 / 48)));

    if (meterPeakFill) meterPeakFill.style.width = peakPct.toFixed(1) + '%';
    if (meterRmsFill) meterRmsFill.style.width = rmsPct.toFixed(1) + '%';
    if (peakDbText) peakDbText.textContent = peakDb.toFixed(1) + ' dB';
    if (rmsDbText) rmsDbText.textContent = rmsDb.toFixed(1) + ' dB';

    // Trigger 3D pulse
    if (window.Vixxo3D && peakVal > 0.15) {
      window.Vixxo3D.triggerPulse(peakVal * 0.7);
    }
  }

  // Event Listeners
  if (startBtn) {
    startBtn.addEventListener('click', () => {
      if (!isPlaying) {
        startSynthesizer();
      } else {
        stopSynthesizer();
      }
    });
  }

  // High-Pass Filter Toggle
  if (toggleHpf) {
    toggleHpf.addEventListener('click', () => {
      stateHpf = !stateHpf;
      toggleHpf.classList.toggle('active', stateHpf);
      if (biquadFilter) {
        biquadFilter.frequency.value = stateHpf ? 80 : 10;
      }
    });
  }

  // Noise Gate Toggle
  if (toggleGate) {
    toggleGate.addEventListener('click', () => {
      stateGate = !stateGate;
      toggleGate.classList.toggle('active', stateGate);
    });
  }

  // Limiter Toggle
  if (toggleLimiter) {
    toggleLimiter.addEventListener('click', () => {
      stateLimiter = !stateLimiter;
      toggleLimiter.classList.toggle('active', stateLimiter);
    });
  }

  // Gain Slider
  if (gainSlider) {
    gainSlider.addEventListener('input', (e) => {
      currentGainDb = parseFloat(e.target.value);
      if (gainValueDisplay) {
        gainValueDisplay.textContent = (currentGainDb > 0 ? '+' : '') + currentGainDb.toFixed(0) + ' dB';
      }
      if (gainNode) {
        gainNode.gain.value = Math.pow(10, currentGainDb / 20);
      }
    });
  }

  // Init grid on load and resize
  window.addEventListener('resize', drawIdleGrid);
  drawIdleGrid();
})();
