// zod shape -> JSON Schema walker, covering exactly the builders the tools
// use: string/number/boolean/enum/array + optional/describe/min.
import type { z } from "zod";

export type JsonSchema = Record<string, unknown>;

function descriptionOf(schema: unknown): string | undefined {
  const s = schema as { description?: unknown } | undefined;
  return typeof s?.description === "string" ? s.description : undefined;
}

function unwrapOptional(schema: unknown): {
  inner: unknown;
  optional: boolean;
} {
  const s = schema as { def?: { type?: string; innerType?: unknown } };
  if (s?.def?.type === "optional") {
    return { inner: s.def.innerType, optional: true };
  }
  return { inner: schema, optional: false };
}

function zodToJson(schema: unknown): JsonSchema {
  const s = schema as {
    def?: {
      type?: string;
      values?: unknown;
      element?: unknown;
      checks?: unknown;
    };
    options?: unknown;
  };
  switch (s.def?.type) {
    case "string":
      return { type: "string" };
    case "number":
      return { type: "number" };
    case "boolean":
      return { type: "boolean" };
    case "enum": {
      const values = Array.isArray(s.def.values)
        ? s.def.values
        : Array.isArray(s.options)
          ? s.options
          : [];
      return { type: "string", enum: values.map(String) };
    }
    case "array": {
      const items = argToJson(s.def.element);
      // zod v4 length checks: { _zod: { def: { check: "min_length", minimum } } }
      const checks = Array.isArray(s.def.checks)
        ? (s.def.checks as Record<string, any>[])
        : [];
      const minLength = checks.find(
        (c) => c?._zod?.def?.check === "min_length",
      );
      const min = minLength?._zod?.def?.minimum;
      return typeof min === "number"
        ? { type: "array", items, minItems: min }
        : { type: "array", items };
    }
    default:
      return { type: "string" };
  }
}

function argToJson(schema: unknown): JsonSchema {
  return zodToJson(schema);
}

export function shapeToJsonSchema(
  shape: Record<string, z.ZodTypeAny>,
): {
  type: "object";
  properties: Record<string, JsonSchema>;
  required: string[];
  additionalProperties: boolean;
} {
  const properties: Record<string, JsonSchema> = {};
  const required: string[] = [];
  for (const [key, raw] of Object.entries(shape)) {
    const { inner, optional } = unwrapOptional(raw);
    const json = argToJson(inner);
    const description = descriptionOf(raw) ?? descriptionOf(inner);
    if (description) json.description = description;
    properties[key] = json;
    if (!optional) required.push(key);
  }
  return {
    type: "object",
    properties,
    required,
    additionalProperties: false,
  };
}
