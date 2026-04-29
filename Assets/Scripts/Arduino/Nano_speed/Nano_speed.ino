/*
 * Continuous Position Control - A4950 Driver
 * * This code is adapted for the A4950 motor driver, which uses
 * a different control scheme than a standard H-bridge with an enable pin.
 * * A4950 Control Logic:
 * IN1   | IN2   | Function
 * ---------------------------
 * PWM   | LOW   | Forward
 * LOW   | PWM   | Reverse
 * LOW   | LOW   | Brake (fast stop)
 * HIGH  | HIGH  | Brake (fast stop)
 * * Motor continuously moves toward the 'targetPosition'.
 * Commands can be received at any time to update the target.
 */

#include <Arduino.h>

// ----------------- Pin Definitions -----------------
// A4950 Motor Driver Pins
const uint8_t A_IN1 = 5;  // A4950 Input 1 (controls direction/speed)
const uint8_t A_IN2 = 6;  // A4950 Input 2 (controls direction/speed)
// ENA pin is NOT needed for A4950

const uint8_t ENC_A_PIN = 3;  // Encoder Channel A (interrupt 0)
const uint8_t ENC_B_PIN = 2;  // Encoder Channel B (interrupt 1)

const uint8_t PRESSURE_PIN = A1; // Restored Pressure Sensor on A1

const uint8_t BUTTON_HOME = 8;
const uint8_t BUTTON_CONTROL = 7;

// ----------------- Global Variables -----------------
volatile long targetPosition = 0;  // Made volatile since it can change anytime

// Movement parameters
const int FAST_SPEED = 255;    // PWM for fast movement
const int SLOW_SPEED = 150;    // PWM for slow approach
const int BRAKE_SPEED = 150;   // PWM for active reverse braking
const int CREEP_SPEED = 100;    // PWM for final approach

const int SLOW_DISTANCE = 900;   // Start slowing down at this distance
const int BRAKE_DISTANCE = 200;   // Distance at which reverse braking is applied
const int CREEP_DISTANCE = 100;   // Final creeping distance
const int STOP_TOLERANCE = 15;   // Stop when within this many counts

const float END_BRAKE_SPEED = 1800.0; // (counts/s) Below this speed, active braking stops and creep begins

// Debugging
bool debugMode = true;
unsigned long lastDebugTime = 0;
const unsigned long DEBUG_INTERVAL = 200; // Print debug every 200ms

unsigned long lastSendTime = 0;
const unsigned long SEND_INTERVAL = 20; // Send every 50ms

// Command parsing
String inputBuffer = "";

// Movement limits
const long MIN_POSITION = 50;    // Minimum allowed position (encoder counts)
const long MAX_POSITION = 10000; // Maximum allowed position (encoder counts)

// Distance conversion constants
const float DISTANCE_SLOPE = 0.0084;  // mm per encoder count
const float DISTANCE_OFFSET = 0.5;    // mm offset
const float MIN_DISTANCE_MM = 0;      // Minimum distance in mm
const float MAX_DISTANCE_MM = 75.0;   // Maximum distance in mm
bool isHoming = false;

// Encoder variables
volatile long encoderCount = 0;
volatile uint8_t lastEncoded = 0;

// ----------------- Control Modes -----------------
enum ControlMode {
  MODE_POSITION, // Default: Move to targetPosition
  MODE_DIRECT    // Direct PWM control
};

ControlMode currentMode = MODE_POSITION;
int directPwm = 0; // -255 to 255 for direct mode

// ----------------- Debounce Variables -----------------
unsigned long debounceDelay = 50; // 50ms debounce

// Control Button
int buttonControlState = HIGH;      // Current stable state
int lastButtonControlReading = HIGH; // Previous immediate reading
unsigned long lastDebounceTimeControl = 0;

// Home Button
int buttonHomeState = HIGH;
int lastButtonHomeReading = HIGH;
unsigned long lastDebounceTimeHome = 0;

// ----------------- Setup -----------------
void setup() {
  Serial.begin(115200);
  Serial.println("=== Continuous Position Control (A4950 Driver) ===");
  Serial.println("Commands:");
  Serial.println("  M<distance> - Move pen to extend distance in mm (e.g., M30.2)");
  Serial.println("  H - Home motor");
  Serial.println("  S - Stop motor (set target to current position)");
  Serial.println("  T - Test motor");
  Serial.println("  E - Test encoder");
  Serial.println("  D - Toggle debug mode");
  Serial.println("  M<distance> - Move pen to extend distance in mm (e.g., M30.2)");
  Serial.println("  A<pwm>      - Direct actuation (-255 to 255) (e.g., A200, A-150)");
  Serial.println("  H - Home motor");
  Serial.println("  S - Stop motor (set target to current position)");
  Serial.println("  T - Test motor");
  Serial.println("  E - Test encoder");
  Serial.println("  D - Toggle debug mode");
  Serial.println("  P - Print current position and distance");
  Serial.println();

  // Pin setup
  pinMode(A_IN1, OUTPUT);
  pinMode(A_IN2, OUTPUT);

  pinMode(BUTTON_HOME, INPUT_PULLUP);
  pinMode(BUTTON_CONTROL, INPUT_PULLUP);
  // pinMode(PRESSURE_PIN, INPUT); // Default is analog input, no pullup needed

  // Initialize motor stopped (braked)
  stopMotor();
  homeMotor();

  // Set initial target to current position (stopped)
  targetPosition = encoderCount;

  // Encoder interrupt
  attachInterrupt(digitalPinToInterrupt(ENC_A_PIN), updateEncoder, CHANGE);
  attachInterrupt(digitalPinToInterrupt(ENC_B_PIN), updateEncoder, CHANGE);

  Serial.println("Setup complete. Motor will continuously track target position.");
  Serial.print("Current position: ");
  Serial.println(encoderCount);

  // Reserve string buffer for commands
  inputBuffer.reserve(50);
}

// ----------------- Main Loop -----------------
void loop() {
  // ALWAYS check for and handle serial commands (non-blocking)
  handleSerialInput();

  // Update Button States (Debounce)
  updateButtons();

  // ALWAYS update motor movement based on current mode
  if (currentMode == MODE_POSITION) {
    updateContinuousMovement();
  } else if (currentMode == MODE_DIRECT) {
    updateDirectMovement();
  }

  // Send sensor data periodically
  unsigned long currentTime = millis();
  if (currentTime - lastSendTime >= SEND_INTERVAL) {
    readSensorData();
    lastSendTime = currentTime;
  }

  // Debug output
  if (debugMode && (millis() - lastDebugTime > DEBUG_INTERVAL)) {
    if (abs(targetPosition - encoderCount) > STOP_TOLERANCE) {
      // printMovementStatus();
    }
    lastDebugTime = millis();
  }
}

void readSensorData() {
  // 1. Read Sensors
  int pressureValue = analogRead(PRESSURE_PIN);
  
  // Use debounced states instead of direct reads
  int digitalValue = buttonControlState;
  int homeValue = buttonHomeState;
  
  float currentDistance = encoderCountsToDistance(encoderCount);
  
  // 2. Send Data
  // Format: P<pressure>|E<enc>|D<dist>|B<btn>|H<home>|C1
  
  Serial.print("P");
  Serial.print(pressureValue); // Output Pressure Value
  Serial.print("|E");
  Serial.print(encoderCount);
  Serial.print("|D");
  Serial.print(currentDistance, 1);
  Serial.print("|B");
  Serial.print(digitalValue);
  Serial.print("|H");
  Serial.print(homeValue);    
  Serial.print("|C");         
  Serial.println(1);          // Hardcoded C to 1
}

// ----------------- Continuous Movement Control -----------------
void updateContinuousMovement() {
  long currentPos = encoderCount;

  // Velocity calculation for state machine
  static unsigned long lastVelocityTime = 0;
  static long lastVelocityEncoder = 0;
  static float currentVelocity = 0.0;
  
  unsigned long now = micros();
  // Update velocity roughly every 5ms (5000us)
  if (now - lastVelocityTime >= 5000) {
    currentVelocity = (float)(currentPos - lastVelocityEncoder) * 1000000.0 / (float)(now - lastVelocityTime);
    lastVelocityEncoder = currentPos;
    lastVelocityTime = now;
  }
  float absSpeed = abs(currentVelocity);

  // Safety check: stop if we've exceeded limits (but not during homing)
  if (!isHoming) {
    if (currentPos < MIN_POSITION && targetPosition >= MIN_POSITION) {
      // We're below minimum and trying to go up - that's OK
    } else if (currentPos > MAX_POSITION && targetPosition <= MAX_POSITION) {
      // We're above maximum and trying to go down - that's OK
    } else if (currentPos < MIN_POSITION && targetPosition < MIN_POSITION) {
      // Below minimum and target is also below - stop
      targetPosition = MIN_POSITION;
    } else if (currentPos > MAX_POSITION && targetPosition > MAX_POSITION) {
      // Above maximum and target is also above - stop
      targetPosition = MAX_POSITION;
    }
  }

  long error = targetPosition - currentPos;
  long absError = abs(error);

  // Check if we're close enough to target (within tolerance)
  if (absError <= STOP_TOLERANCE) {
    stopMotor();
    return;
  }

  // Determine base direction (1 = forward, -1 = reverse)
  int base_direction = (error > 0) ? 1 : -1;
  int apply_direction = base_direction;
  int speed = FAST_SPEED;
  
  // Phase state machine for movement
  enum MovePhase { PHASE_CRUISE, PHASE_BRAKE, PHASE_CREEP };
  static MovePhase movePhase = PHASE_CRUISE;

  if (absError > BRAKE_DISTANCE) {
    movePhase = PHASE_CRUISE;
  } else {
    // Inside the braking zone
    if (movePhase == PHASE_CRUISE) {
      if (absSpeed > END_BRAKE_SPEED) {
        movePhase = PHASE_BRAKE; // Too fast, trigger active braking
      } else {
        movePhase = PHASE_CREEP; // Already slow enough, skip braking
      }
    } else if (movePhase == PHASE_BRAKE) {
      // Monitor speed and stop braking once it drops sufficiently
      if (absSpeed <= END_BRAKE_SPEED) {
        movePhase = PHASE_CREEP;
      }
    }
  }

  // Execute current phase
  if (movePhase == PHASE_CRUISE) {
    apply_direction = base_direction;
    if (absError <= SLOW_DISTANCE) {
      speed = SLOW_SPEED;
    } else {
      speed = FAST_SPEED;
    }
  } else if (movePhase == PHASE_BRAKE) {
    apply_direction = -base_direction; // Active reverse
    speed = BRAKE_SPEED;
  } else if (movePhase == PHASE_CREEP) {
    apply_direction = base_direction;
    speed = CREEP_SPEED;
  }

  // Apply movement using A4950 logic
  setMotor(apply_direction, speed);

}

// ----------------- Direct Movement Control -----------------
void updateDirectMovement() {
  long currentPos = encoderCount;
  
  // Safety checks for limits
  // Forward (positive PWM) increases position
  if (directPwm > 0 && currentPos >= MAX_POSITION) {
    setMotor(0, 0); // Hard stop at max
    return;
  }
  
  // Reverse (negative PWM) decreases position
  if (directPwm < 0 && currentPos <= MIN_POSITION) {
    setMotor(0, 0); // Hard stop at min
    return;
  }
  
  // Apply direct PWM
  int dir = 0;
  if (directPwm > 0) dir = 1;
  else if (directPwm < 0) dir = -1;
  
  setMotor(dir, abs(directPwm));
}

// ----------------- A4950 Motor Control Functions -----------------

/**
 * @brief Sets the motor direction and speed for the A4950.
 * @param direction 1 for forward (positive encoder count), -1 for reverse (negative).
 * @param speed The PWM value (0-255).
 */
void setMotor(int direction, int speed) {
  // Ensure speed is within valid PWM range
  speed = constrain(speed, 0, 255);

  if (direction > 0) {
    // Forward: IN1 = PWM, IN2 = LOW
    analogWrite(A_IN1, speed);
    digitalWrite(A_IN2, LOW);
  } else if (direction < 0) {
    // Reverse: IN1 = LOW, IN2 = PWM
    digitalWrite(A_IN1, LOW);
    analogWrite(A_IN2, speed);
  } else {
    // Direction is 0, so stop
    stopMotor();
  }
}

/**
 * @brief Stops the motor using the A4950 brake function.
 * Sets both IN1 and IN2 to LOW.
 */
void stopMotor() {
  digitalWrite(A_IN1, LOW);
  digitalWrite(A_IN2, LOW);
  // Ensure no PWM signals are active
  analogWrite(A_IN1, 0);
  analogWrite(A_IN2, 0);
}

// ----------------- Position & Distance Functions -----------------

long distanceToEncoderCounts(float distanceMM) {
  return (long)((distanceMM - DISTANCE_OFFSET) / DISTANCE_SLOPE);
}

float encoderCountsToDistance(long counts) {
  return (counts * DISTANCE_SLOPE) + DISTANCE_OFFSET;
}

void setTargetDistance(float distanceMM) {
  if (distanceMM < MIN_DISTANCE_MM) {
    distanceMM = MIN_DISTANCE_MM;
    Serial.print("Distance clamped to minimum: ");
  } else if (distanceMM > MAX_DISTANCE_MM) {
    distanceMM = MAX_DISTANCE_MM;
    Serial.print("Distance clamped to maximum: ");
  }

  long targetCounts = distanceToEncoderCounts(distanceMM);
  setTargetPosition(targetCounts);

  Serial.print("Target distance: ");
  Serial.print(distanceMM, 1);
  Serial.print("mm (counts: ");
  Serial.print(targetCounts);
  Serial.println(")");
}

void setTargetPosition(long target) {
  // Ensure we are in Position Mode when setting a target
  currentMode = MODE_POSITION;

  if (!isHoming) {
    if (target < MIN_POSITION) {
      target = MIN_POSITION;
    } else if (target > MAX_POSITION) {
      target = MAX_POSITION;
    }
  }

  // Immediately update target - motor will start moving toward it on next loop
  targetPosition = target;

  Serial.print("New target: ");
  Serial.print(target);
  Serial.print(" (current: ");
  Serial.print(encoderCount);
  Serial.print(", distance: ");
  Serial.print(abs(target - encoderCount));
  Serial.println(")");
}

void stopMovement() {
  // Set target to current position to stop movement
  currentMode = MODE_POSITION; // Revert to position holding
  targetPosition = encoderCount;
  stopMotor();
  Serial.println("Movement stopped. Target set to current position.");
}

// ----------------- Non-blocking Serial Input Handler -----------------
void handleSerialInput() {
  // Read available characters without blocking
  while (Serial.available() > 0) {
    char inChar = Serial.read();

    if (inChar == '\n' || inChar == '\r') {
      if (inputBuffer.length() > 0) {
        processCommand(inputBuffer);
        inputBuffer = "";  // Clear buffer
      }
    } else {
      inputBuffer += inChar;
    }
  }
}

void processCommand(String command) {
  command.trim();
  command.toUpperCase();

  if (command.length() == 0) return;

  char cmd = command.charAt(0);

  switch (cmd) {
    case 'M': {
      // Switch to Position Mode
      currentMode = MODE_POSITION;
      if (command.length() > 1) {
        float distanceMM = command.substring(1).toFloat();
        // Allow M0 to go to home position
        if (distanceMM >= 0) {
          setTargetDistance(distanceMM);
        } else {
          Serial.println("Invalid distance. Must be positive.");
        }
      } else {
        Serial.println("Usage: M<distance> (e.g., M30.2 for 30.2mm)");
      }
      break;
    }

    case 'A': {
      // Switch to Direct Mode
      if (command.length() > 1) {
        int pwmVal = command.substring(1).toInt();
        // Constrain input just in case
        pwmVal = constrain(pwmVal, -255, 255);
        
        currentMode = MODE_DIRECT;
        directPwm = pwmVal;
        
        Serial.print("Direct Mode: PWM ");
        Serial.println(directPwm);
      } else {
        Serial.println("Usage: A<pwm> (e.g., A200, A-150)");
      }
      break;
    }

    case 'H':
      // Homing will take over control, reset to Position mode at end
      currentMode = MODE_POSITION; 
      homeMotor();
      break;

    case 'S':
      stopMovement(); // Sets target to current, effectively switching to Position Hold
      break;

    case 'T':
      currentMode = MODE_POSITION;
      testMotor();
      break;

    case 'E':
      // Test encoder usually stops motor first
      testEncoder();
      break;

    case 'D':
      debugMode = !debugMode;
      Serial.print("Debug mode: ");
      Serial.println(debugMode ? "ON" : "OFF");
      break;

    case 'P': {
      printStatus(); // Use the helper function
      break;
    }

    default:
      Serial.print("Unknown command: ");
      Serial.println(command);
      Serial.println("Use H, M<distance>, A<pwm>, S, T, E, D, or P");
  }
}

// ----------------- Homing -----------------
void homeMotor() {
  Serial.println("Homing motor...");
  isHoming = true;

  // Set target far in reverse direction to start homing movement
  targetPosition = -10000;  // This will make it move toward home

  // Wait for button press while allowing continuous movement
  while (digitalRead(BUTTON_HOME) == HIGH) {
    // updateContinuousMovement();  // Keep moving during homing
    
    // --- CHANGE HERE ---
    // Instead of using the main movement logic, we'll
    // manually set the motor to move in reverse at SLOW_SPEED.
    // You can change this to CREEP_SPEED if you want it even slower.
    setMotor(-1, CREEP_SPEED); 
    // --- END CHANGE ---

    delay(10);

    // Safety check - print position occasionally
    static unsigned long lastHomePrint = 0;
    if (millis() - lastHomePrint > 500) {
      Serial.print("Homing... position: ");
      Serial.println(encoderCount);
      lastHomePrint = millis();
    }
  }

  // Button pressed - continue moving for 0.5 seconds
  Serial.println("Home button triggered.");

 
  // Stop motor
  stopMotor();
  delay(100);

  // Reset encoder and target
  encoderCount = 0;
  targetPosition = 0;  // Start at home position
  isHoming = false;

  Serial.println("Homing complete. Position reset to 0.");
  Serial.println("Ready for operation.");
}
// ----------------- Test Functions -----------------
void testMotor() {
  Serial.println("=== Motor Test ===");

  Serial.println("Moving to 10mm...");
  setTargetDistance(10.0);
  delay(3000);  // Let it move for 3 seconds

  Serial.println("Moving to 25mm...");
  setTargetDistance(25.0);
  delay(3000);  // Let it move for 3 seconds

  Serial.println("Returning to 5mm...");
  setTargetDistance(5.0);
  delay(2000);

  Serial.println("Motor test complete.");
  float finalDistance = encoderCountsToDistance(encoderCount);
  Serial.print("Final position: ");
  Serial.print(encoderCount);
  Serial.print(" counts (");
  Serial.print(finalDistance, 1);
  Serial.println("mm)");
}

void testEncoder() {
  Serial.println("=== Encoder Test ===");
  Serial.println("Manually turn the motor shaft...");
  Serial.println("Watching for 10 seconds...");

  long startCount = encoderCount;
  unsigned long startTime = millis();
  long lastPrintedCount = startCount;

  // Stop motor movement during encoder test
  long savedTarget = targetPosition;
  targetPosition = encoderCount; // Set target to current to stop movement
  stopMotor(); // Explicitly stop motor

  while (millis() - startTime < 10000) {
    if (encoderCount != lastPrintedCount) {
      Serial.print("Encoder: ");
      Serial.print(encoderCount);
      Serial.print(" (change: ");
      Serial.print(encoderCount - lastPrintedCount);
      Serial.println(")");
      lastPrintedCount = encoderCount;
    }
    delay(50);
  }

  // Restore target
  targetPosition = savedTarget;

  Serial.print("Encoder test complete. Total change: ");
  Serial.println(encoderCount - startCount);
}

// ----------------- Debug Functions -----------------
void printMovementStatus() {
  long error = targetPosition - encoderCount;
  Serial.print("Pos: ");
  Serial.print(encoderCount);
  Serial.print(" | Target: ");
  Serial.print(targetPosition);
  Serial.print(" | Error: ");
  Serial.print(error);
  Serial.print(" | Distance: ");
  Serial.println(abs(error));
}

// ----------------- Encoder ISR -----------------
void updateEncoder() {
  // State table for quadrature decoding
  // Lookup table based on previous and current AB states
  static const int8_t encoderStates[16] = {
      0, -1,  1,  0,
      1,  0,  0, -1,
     -1,  0,  0,  1,
      0,  1, -1,  0
  };

  // Read current state of both pins
  uint8_t a = digitalRead(ENC_A_PIN);
  uint8_t b = digitalRead(ENC_B_PIN);

  // Create 2-bit current state
  uint8_t currentEncoded = (a << 1) | b;

  // Combine with last state to create 4-bit lookup value
  uint8_t sum = (lastEncoded << 2) | currentEncoded;

  // Update count based on state transition
  encoderCount += encoderStates[sum];

  // Auto-Homing Anti-Slip Protection: Calibrate to 0 firmly on hard collision
  if (encoderCount < 0) {
    if (digitalRead(BUTTON_HOME) == LOW) { // Active LOW
      encoderCount = 0;
    }
  }

  // Save current state for next time
  lastEncoded = currentEncoded;
}


// ----------------- Additional Helper Functions -----------------
void emergencyStop() {
  stopMotor();
  targetPosition = encoderCount;  // Set target to current position
  Serial.println("EMERGENCY STOP!");
}

void printStatus() {
  float currentDistance = encoderCountsToDistance(encoderCount);
  float targetDistance = encoderCountsToDistance(targetPosition);

  Serial.println("=== Status ===");
  Serial.print("Position: ");
  Serial.print(encoderCount);
  Serial.print(" counts (");
  Serial.print(currentDistance, 1);
  Serial.println("mm)");
  Serial.print("Target: ");
  Serial.print(targetPosition);
  Serial.print(" counts (");
  Serial.print(targetDistance, 1);
  Serial.println("mm)");
  Serial.print("Error: ");
  Serial.print(targetPosition - encoderCount);
  Serial.print(" counts (");
  Serial.print(targetDistance - currentDistance, 1);
  Serial.println("mm)");
  Serial.print("Moving: ");
  Serial.println(abs(targetPosition - encoderCount) > STOP_TOLERANCE ? "YES" : "NO");
  Serial.print("Mode: ");
  Serial.println(currentMode == MODE_POSITION ? "POSITION" : "DIRECT");
  if (currentMode == MODE_DIRECT) {
     Serial.print("Direct PWM: ");
     Serial.println(directPwm);
  }
  Serial.print("Debug: ");
  Serial.println(debugMode ? "ON" : "OFF");
  Serial.println("===============");
}

void updateButtons() {
  // --- Control Button ---
  int readingControl = digitalRead(BUTTON_CONTROL);
  
  if (readingControl != lastButtonControlReading) {
    lastDebounceTimeControl = millis();
  }
  
  if ((millis() - lastDebounceTimeControl) > debounceDelay) {
    // Determine if state has changed
    if (readingControl != buttonControlState) {
      buttonControlState = readingControl;
    }
  }
  lastButtonControlReading = readingControl;

  // --- Home Button ---
  int readingHome = digitalRead(BUTTON_HOME);
  
  if (readingHome != lastButtonHomeReading) {
    lastDebounceTimeHome = millis();
  }
  
  if ((millis() - lastDebounceTimeHome) > debounceDelay) {
    if (readingHome != buttonHomeState) {
      buttonHomeState = readingHome;
    }
  }
  lastButtonHomeReading = readingHome;
}
