(async function evaluateFunction(fn, argument) {
  if (typeof fn !== "function") {
    throw new TypeError(
      "JavaScript with arguments must evaluate to a function",
    );
  }

  return await fn(argument);
});
