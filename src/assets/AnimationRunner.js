(async function runAnimation(durationMs, update, easing) {
  if (typeof update !== "function") {
    throw new TypeError("update must evaluate to a function");
  }

  if (typeof easing !== "function") {
    throw new TypeError("easing must evaluate to a function");
  }

  const updateDurations = [];
  const started = performance.now();

  return await new Promise((resolve, reject) => {
    const tick = (now) => {
      try {
        const linear = Math.min(1, Math.max(0, (now - started) / durationMs));

        const progress = easing(linear);

        if (!Number.isFinite(progress)) {
          throw new TypeError("easing returned a non-finite value");
        }

        const before = performance.now();

        const result = update(
          Object.freeze({
            progress,
            linear_progress: linear,
            elapsed_ms: Math.min(now - started, durationMs),
            duration_ms: durationMs,
          }),
        );

        if (result && typeof result.then === "function") {
          throw new TypeError("update must be synchronous");
        }

        updateDurations.push(performance.now() - before);

        if (linear >= 1) {
          resolve({
            update_durations_ms: updateDurations,
          });
        } else {
          requestAnimationFrame(tick);
        }
      } catch (error) {
        reject(error);
      }
    };

    requestAnimationFrame(tick);
  });
});
