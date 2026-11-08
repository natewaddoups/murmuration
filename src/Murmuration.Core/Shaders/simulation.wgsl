struct SimulationFrame {
    count: u32,
    delta: f32,
    step: u32,
    firstNewBird: u32,
    seed: u32,
    padding0: u32,
    padding1: u32,
    padding2: u32,
};

@group(0) @binding(0) var<uniform> simulation: SimulationFrame;
@group(0) @binding(1) var<storage, read> birdsIn: array<GpuBird>;
@group(0) @binding(2) var<storage, read_write> birdsOut: array<GpuBird>;
@group(0) @binding(3) var<storage, read_write> instances: array<f32>;
var<workgroup> neighborPositions: array<vec4<f32>, 64>;
var<workgroup> neighborDirections: array<vec4<f32>, 64>;

fn rotate(vector: vec3<f32>, orientation: vec4<f32>) -> vec3<f32> {
    let twiceCross = 2.0 * cross(orientation.xyz, vector);
    return vector + orientation.w * twiceCross + cross(orientation.xyz, twiceCross);
}

fn steering(direction: vec3<f32>, desired: vec3<f32>) -> vec3<f32> {
    if (dot(desired, desired) < 1e-12) { return vec3<f32>(0.0); }
    return normalize(desired) - direction;
}

fn clampSlope(desired: vec3<f32>, current: vec3<f32>) -> vec3<f32> {
    let maxSin = select(MaxDescentSin, MaxAscentSin, desired.z > 0.0);
    if (abs(desired.z) <= maxSin) { return desired; }
    var horizontal = desired.xy + current.xy;
    if (dot(horizontal, horizontal) < 1e-12) { horizontal = vec2<f32>(1.0, 0.0); }
    horizontal = normalize(horizontal) * sqrt(1.0 - maxSin * maxSin);
    return vec3<f32>(horizontal, select(-maxSin, maxSin, desired.z >= 0.0));
}

fn levelLeft(forward: vec3<f32>, fallback: vec3<f32>) -> vec3<f32> {
    let levelUp = vec3<f32>(0.0, 0.0, 1.0) - forward * forward.z;
    if (dot(levelUp, levelUp) < 1e-12) { return fallback; }
    return normalize(cross(normalize(levelUp), forward));
}

fn axisAngle(axis: vec3<f32>, angle: f32) -> vec4<f32> {
    return vec4<f32>(axis * sin(angle * 0.5), cos(angle * 0.5));
}

fn multiplyQuaternion(first: vec4<f32>, second: vec4<f32>) -> vec4<f32> {
    return vec4<f32>(first.w * second.xyz + second.w * first.xyz + cross(first.xyz, second.xyz),
        first.w * second.w - dot(first.xyz, second.xyz));
}

fn orient(orientation: vec4<f32>, desiredDirection: vec3<f32>, speed: f32, delta: f32) -> vec4<f32> {
    if (dot(desiredDirection, desiredDirection) < 1e-12) { return orientation; }
    let desired = normalize(desiredDirection);
    var result = orientation;
    var forward = rotate(vec3<f32>(1.0, 0.0, 0.0), result);
    var left = rotate(vec3<f32>(0.0, 1.0, 0.0), result);
    let maxTurnRate = min(MaxPitchRate, Gravity * tan(MaxBank) / max(speed, 0.1));
    let turnDir = desired - forward * dot(desired, forward);
    let turnAngle = atan2(length(turnDir), dot(desired, forward));
    let turnRate = min(turnAngle / delta, maxTurnRate);
    var targetUp = vec3<f32>(0.0, 0.0, Gravity);
    if (dot(turnDir, turnDir) > 1e-12) { targetUp += normalize(turnDir) * (speed * turnRate); }
    targetUp.z = max(targetUp.z, 0.0);
    targetUp -= forward * dot(targetUp, forward);
    var targetLeft = levelLeft(forward, left);
    if (dot(targetUp, targetUp) > 1e-12) { targetLeft = normalize(cross(targetUp, forward)); }
    let level = levelLeft(forward, left);
    let bank = atan2(dot(forward, cross(level, targetLeft)), dot(level, targetLeft));
    let clampedBank = clamp(bank, -MaxBank, MaxBank);
    if (clampedBank != bank) { targetLeft = rotate(level, axisAngle(forward, clampedBank)); }
    let rollAngle = atan2(dot(forward, cross(left, targetLeft)), dot(left, targetLeft));
    let rollDelta = clamp(rollAngle, -MaxRollRate * delta, MaxRollRate * delta);
    if (abs(rollDelta) > 1e-7) { result = normalize(multiplyQuaternion(axisAngle(forward, rollDelta), result)); }

    forward = rotate(vec3<f32>(1.0, 0.0, 0.0), result);
    left = rotate(vec3<f32>(0.0, 1.0, 0.0), result);
    let perpZ = vec3<f32>(0.0, 0.0, 1.0) - forward * forward.z;
    let cosTheta = length(perpZ);
    if (cosTheta > 1e-3) {
        let perpZHat = perpZ / cosTheta;
        let up = cross(forward, left);
        let cosBank = max(dot(up, perpZHat), cos(MaxBank));
        let net = Gravity * cosTheta * (up / cosBank - perpZHat);
        let netLength = length(net);
        if (netLength > 1e-6) {
            let rate = min(netLength / max(speed, MinSpeed), maxTurnRate);
            let axis = normalize(cross(forward, net));
            result = normalize(multiplyQuaternion(axisAngle(axis, rate * delta), result));
        }
    }

    forward = rotate(vec3<f32>(1.0, 0.0, 0.0), result);
    left = rotate(vec3<f32>(0.0, 1.0, 0.0), result);
    let turnAxis = cross(forward, desired);
    let pitchAngle = atan2(dot(left, turnAxis), dot(forward, desired));
    let pitchDelta = clamp(pitchAngle, -maxTurnRate * delta, maxTurnRate * delta);
    if (abs(pitchDelta) > 1e-7) { result = normalize(multiplyQuaternion(axisAngle(left, pitchDelta), result)); }
    forward = rotate(vec3<f32>(1.0, 0.0, 0.0), result);
    let limited = clampSlope(forward, forward);
    let fix = cross(forward, limited);
    if (dot(fix, fix) > 1e-12) {
        let angle = atan2(length(fix), dot(forward, limited));
        result = normalize(multiplyQuaternion(axisAngle(normalize(fix), angle), result));
    }
    return result;
}

fn randomValue(index: u32, step: u32) -> f32 {
    var value = index ^ (step * 747796405u + 2891336453u);
    value = ((value >> ((value >> 28u) + 4u)) ^ value) * 277803737u;
    value = (value >> 22u) ^ value;
    return f32(value >> 8u) * (1.0 / 16777216.0);
}

fn initialOrientation(forward: vec3<f32>) -> vec4<f32> {
    var up = vec3<f32>(0.0, 0.0, 1.0) - forward * forward.z;
    if (dot(up, up) < 1e-12) {
        up = select(vec3<f32>(1.0, 0.0, 0.0), vec3<f32>(0.0, 0.0, 1.0), abs(forward.z) < 0.9);
        up -= forward * dot(up, forward);
    }
    up = normalize(up);
    let left = normalize(cross(up, forward));
    up = cross(forward, left);
    let trace = forward.x + left.y + up.z;
    var orientation: vec4<f32>;
    if (trace > 0.0) {
        let scale = sqrt(trace + 1.0) * 2.0;
        orientation = vec4<f32>((left.z - up.y) / scale, (up.x - forward.z) / scale,
            (forward.y - left.x) / scale, scale * 0.25);
    } else if (forward.x > left.y && forward.x > up.z) {
        let scale = sqrt(1.0 + forward.x - left.y - up.z) * 2.0;
        orientation = vec4<f32>(scale * 0.25, (left.x + forward.y) / scale,
            (up.x + forward.z) / scale, (left.z - up.y) / scale);
    } else if (left.y > up.z) {
        let scale = sqrt(1.0 + left.y - forward.x - up.z) * 2.0;
        orientation = vec4<f32>((left.x + forward.y) / scale, scale * 0.25,
            (up.y + left.z) / scale, (up.x - forward.z) / scale);
    } else {
        let scale = sqrt(1.0 + up.z - forward.x - left.y) * 2.0;
        orientation = vec4<f32>((up.x + forward.z) / scale, (up.y + left.z) / scale,
            scale * 0.25, (forward.y - left.x) / scale);
    }
    return normalize(orientation);
}

fn initialRandom(index: u32, stream: u32) -> f32 {
    return randomValue(index ^ simulation.seed, simulation.step + stream * 2654435769u);
}

@compute @workgroup_size(64)
fn initialize(@builtin(global_invocation_id) id: vec3<u32>) {
    let index = simulation.firstNewBird + id.x;
    if (index >= simulation.count) { return; }
    var direction = vec3<f32>(initialRandom(index, 0u), initialRandom(index, 1u), initialRandom(index, 2u)) * 2.0 - vec3<f32>(1.0);
    if (dot(direction, direction) < 1e-12) { direction = vec3<f32>(1.0, 0.0, 0.0); }
    direction = normalize(direction);
    let preferredSpeed = 3.0 + initialRandom(index, 3u) * 0.25;
    var position = vec3<f32>(30.0 + initialRandom(index, 4u) * 20.0,
        40.0 + initialRandom(index, 5u) * 10.0, 0.0);
    if (simulation.firstNewBird == 0u && simulation.step == 0u) {
        position = vec3<f32>(initialRandom(index, 4u) * 100.0 - 50.0,
            initialRandom(index, 5u) * 100.0 - 50.0, initialRandom(index, 6u) * SoftCeiling);
    }
    var bird: GpuBird;
    bird.Position = vec4<f32>(position, 0.0);
    bird.Orientation = initialOrientation(direction);
    bird.DesiredDirection = vec4<f32>(direction, 0.0);
    bird.Flight = vec4<f32>(preferredSpeed, preferredSpeed, 0.25 + initialRandom(index, 7u) * 0.25,
        0.7 + initialRandom(index, 8u) * 0.5);
    bird.DirectionX = direction.x;
    bird.DirectionY = direction.y;
    bird.DirectionZ = direction.z;
    bird.Flap = vec4<f32>(FlapMinRate + initialRandom(index, 9u) * (FlapMaxRate - FlapMinRate),
        initialRandom(index, 10u) * TAU, 0.0, 0.0);
    birdsOut[index] = bird;
}

fn updateBird(index: u32, input: GpuBird, delta: f32, localIndex: u32) -> GpuBird {
    var bird = input;
    let position = bird.Position.xyz;
    let direction = vec3<f32>(bird.DirectionX, bird.DirectionY, bird.DirectionZ);
    var toCenter = vec3<f32>(0.0);
    var toNeighbor = vec3<f32>(0.0);
    var alignment = vec3<f32>(0.0);
    var climbYaw = vec3<f32>(0.0);
    var vertical = vec3<f32>(0.0);
    let stalled = bird.Flight.x < StallSpeed;
    if (stalled) {
        var level = vec3<f32>(direction.xy, 0.0);
        if (dot(level, level) < 1e-6) { level = vec3<f32>(1.0, 0.0, 0.0); }
        vertical = steering(direction, level);
    } else if (position.z < SoftFloor) {
        vertical = steering(direction, vec3<f32>(0.0, 0.0, 1.0));
    } else if (position.z > SoftCeiling) {
        vertical = steering(direction, vec3<f32>(0.0, 0.0, -1.0));
    }
    if (!stalled) {
        if (direction.z > 0.0 && bird.Climb != 0u) {
            let turn = select(-1.0, 1.0, bird.Climb == 1u);
            let up = rotate(vec3<f32>(0.0, 0.0, 1.0), bird.Orientation);
            climbYaw = cross(up, direction) * (turn * bird.Flight.w);
        } else {
            let offset = Center - position;
            if (abs(offset.x) + abs(offset.y) + abs(offset.z) > CenterRadius) {
                toCenter = steering(direction, offset) * 0.5;
            }
        }
    }

    var best: array<u32, NeighborCount>;
    var distances: array<f32, NeighborCount>;
    var found = 0u;
    for (var base = 0u; base < simulation.count; base += 64u) {
        let loaded = base + localIndex;
        if (loaded < simulation.count) {
            let other = birdsIn[loaded];
            neighborPositions[localIndex] = other.Position;
            neighborDirections[localIndex] = vec4<f32>(other.DirectionX, other.DirectionY, other.DirectionZ, 0.0);
        }
        workgroupBarrier();
        for (var tileIndex = 0u; tileIndex < 64u && base + tileIndex < simulation.count; tileIndex++) {
            let candidate = base + tileIndex;
            if (candidate == index) { continue; }
            let offset = neighborPositions[tileIndex].xyz - position;
            let distance = dot(offset, offset);
            if (distance > NeighborRange * NeighborRange) { continue; }
            if (found == NeighborCount && distance >= distances[found - 1u]) { continue; }
            if (dot(offset, direction) <= 0.0) { continue; }
            if (dot(neighborDirections[tileIndex].xyz, direction) < MinNeighborAlignment) { continue; }
            var slot = NeighborCount - 1u;
            if (found < NeighborCount) { slot = found; found++; }
            while (slot > 0u && distances[slot - 1u] > distance) {
                best[slot] = best[slot - 1u];
                distances[slot] = distances[slot - 1u];
                slot--;
            }
            best[slot] = candidate;
            distances[slot] = distance;
        }
        workgroupBarrier();
    }
    if (found > 0u && distances[0] >= 1e-12) {
        var sum = vec3<f32>(0.0);
        var directionSum = vec3<f32>(0.0);
        for (var neighbor = 0u; neighbor < found; neighbor++) {
            let other = birdsIn[best[neighbor]];
            sum += other.Position.xyz;
            directionSum += vec3<f32>(other.DirectionX, other.DirectionY, other.DirectionZ);
        }
        var targetDirection = direction;
        if (dot(directionSum, directionSum) > 1e-12) { targetDirection = normalize(directionSum); }
        var targetPosition = vec3<f32>(0.0);
        var hasTarget = true;
        if (distances[0] <= CloseNeighborRange * CloseNeighborRange) {
            let closest = birdsIn[best[0]];
            let closestDirection = vec3<f32>(closest.DirectionX, closest.DirectionY, closest.DirectionZ);
            let offset = position - closest.Position.xyz;
            var plane = offset - dot(offset, closestDirection) * closestDirection;
            if (dot(plane, plane) < 1e-12) {
                let axis = select(vec3<f32>(0.0, 1.0, 0.0), vec3<f32>(0.0, 0.0, 1.0), abs(closestDirection.z) < 0.9);
                plane = cross(closestDirection, axis);
            }
            targetPosition = closest.Position.xyz + normalize(plane) * NeighborDistance;
        } else {
            let average = sum / f32(found);
            let toAverage = average - position;
            if (dot(toAverage, toAverage) < 1e-12) {
                hasTarget = false;
            } else {
                targetPosition = average - normalize(toAverage) * NeighborDistance;
            }
        }
        if (hasTarget) {
            let targetDistance = distance(position, targetPosition);
            if (targetDistance > CloseNeighborRange) {
                bird.Flight.x = min(MaxSpeed, bird.Flight.x * (1.0 + FlockSpeedAdjustmentRate * delta));
            } else if (targetDistance < CloseNeighborRange) {
                bird.Flight.x = max(bird.Flight.y, bird.Flight.x * (1.0 - FlockSpeedAdjustmentRate * delta));
            }
            toNeighbor = steering(direction, targetPosition - position);
            alignment = steering(direction, targetDirection);
            if (targetDistance > CloseNeighborRange) { alignment *= 0.5; }
            if (targetDistance > CloseNeighborRange * 2.0) { alignment *= 0.5; }
            if (stalled) { toNeighbor.z = 0.0; alignment.z = 0.0; }
        }
    }

    let sum = direction * 0.5 + toCenter + toNeighbor + alignment + climbYaw + vertical;
    var desired = direction;
    if (dot(sum, sum) > 1e-12) { desired = normalize(sum); }
    desired = clampSlope(desired, direction);
    bird.DesiredDirection = vec4<f32>(desired, 0.0);
    bird.Orientation = orient(bird.Orientation, desired, bird.Flight.x, delta);
    let newDirection = rotate(vec3<f32>(1.0, 0.0, 0.0), bird.Orientation);
    bird.DirectionX = newDirection.x;
    bird.DirectionY = newDirection.y;
    bird.DirectionZ = newDirection.z;
    if (abs(newDirection.z) <= LevelSlope && bird.Flight.x <= bird.Flight.y) {
        bird.Flight.x += (bird.Flight.y - bird.Flight.x) * (1.0 - exp(-SpeedRelaxRate * delta));
    } else {
        bird.Flight.x -= SlopeSpeedGain * bird.Flight.x * newDirection.z * delta;
    }
    bird.Flight.x = clamp(bird.Flight.x, MinSpeed, MaxSpeed);
    bird.Position = vec4<f32>(position + newDirection * bird.Flight.x * delta, 0.0);
    bird.Flap.y = (bird.Flap.y + TAU * bird.Flap.x * delta) % TAU;
    if (direction.z <= 0.0 && newDirection.z > 0.0) {
        let roll = randomValue(index ^ simulation.seed, simulation.step);
        bird.Climb = select(select(0u, 2u, roll < ClimbSpreadFraction * 2.0), 1u, roll < ClimbSpreadFraction);
    }
    return bird;
}

@compute @workgroup_size(64)
fn simulate(@builtin(global_invocation_id) id: vec3<u32>, @builtin(local_invocation_index) localIndex: u32) {
    let index = id.x;
    var bird: GpuBird;
    if (index < simulation.count) { bird = birdsIn[index]; }
    if (simulation.delta > 0.0) { bird = updateBird(index, bird, simulation.delta, localIndex); }
    if (index >= simulation.count) { return; }
    birdsOut[index] = bird;
    let direction = vec3<f32>(bird.DirectionX, bird.DirectionY, bird.DirectionZ);
    let up = rotate(vec3<f32>(0.0, 0.0, 1.0), bird.Orientation);
    let offset = index * 10u;
    instances[offset] = bird.Position.x;
    instances[offset + 1u] = bird.Position.y;
    instances[offset + 2u] = bird.Position.z;
    instances[offset + 3u] = direction.x;
    instances[offset + 4u] = direction.y;
    instances[offset + 5u] = direction.z;
    instances[offset + 6u] = up.x;
    instances[offset + 7u] = up.y;
    instances[offset + 8u] = up.z;
    instances[offset + 9u] = FlapAmplitude * sin(bird.Flap.y);
}