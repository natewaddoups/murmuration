struct Frame {
    viewProj: mat4x4<f32>,
};

@group(0) @binding(0) var<uniform> frame: Frame;

struct VSIn {
    @location(0) localPos: vec3<f32>,
    @location(1) color: vec3<f32>,
    @location(2) instPos: vec3<f32>,
    @location(3) instDir: vec3<f32>,
    @location(4) instUp: vec3<f32>,
};

struct VSOut {
    @builtin(position) pos: vec4<f32>,
    @location(0) color: vec3<f32>,
};

// Local axes: x forward, y left, z up.
@vertex
fn vs(i: VSIn) -> VSOut {
    let fwd = normalize(i.instDir);
    let up = normalize(i.instUp);
    let left = cross(up, fwd);

    let world = i.instPos + fwd * i.localPos.x + left * i.localPos.y + up * i.localPos.z;

    var o: VSOut;
    // System.Numerics matrices are row-major; WGSL reads those bytes as the transpose, so multiply as M * v.
    o.pos = frame.viewProj * vec4<f32>(world, 1.0);
    o.color = i.color;
    return o;
}

@fragment
fn fs(i: VSOut) -> @location(0) vec4<f32> {
    return vec4<f32>(i.color, 1.0);
}
