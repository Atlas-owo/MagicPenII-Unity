**Context:**
I have an existing Unity project with a script named `JNDTestController`. I need to implement a new feature called the **C/D Ratio Test Controller**. Please generate the C# scripts based on the following detailed specifications.

#### 1. C/D Ratio Manipulator Script
Create a helper script (or class) responsible for applying the Control/Display (C/D) ratio.
* **Inputs:** A `float` value for `ratio`, a reference `NurbsSurface` object, a reference `GameObject` (O1), a target `GameObject` (O2), and a reference `Plane` (surface).
* **NURBS Logic:** Read the amplitude ($a_1$) of the reference `NurbsSurface`. Instantiate or modify a target NURBS object at the same position with a new amplitude $a_2$, where $a_2 = a_1 \times ratio$.
* **Object Logic:**
    * Calculate the vertical distance ($d_1$) from O1 to the reference Plane (along the Y-axis).
    * Set the position of O2 such that its distance to the Plane ($d_2$) equals $d_1 \times ratio$.
    * **Constraint:** O2 must strictly match the X and Z world coordinates of O1; only the Y coordinate changes based on the ratio.

#### 2. C/D Ratio Test Controller Script
Create a main controller script (`CDRatioTestController`) derived from or similar in structure to the existing `JNDTestController`.
* **Core Loop:** The experiment follows this cycle: `Show Stimulus` -> `User Response Q1` -> `User Response Q2` -> `Next Trial`.
* **Stimulus Phase:**
    * Unlike the JND test (which compares two stimuli), this test shows **only one stimulus** per trial.
    * Use the existing "Pacing Dot" logic to guide the user in feeling the NURBS surface.
    * The variables changing per trial are the **NURBS Amplitude** and the **C/D Ratio** (applied using the Manipulator script described above).
* **Response Phase (UI & Input):**
    * **Question 1:** "Do you feel the illusion?"
        * Input: Press **'Y'** for Yes, **'N'** for No.
    * **Question 2:** "How confident are you? (Scale 1-5)"
        * UI: Display a slider or cursor starting at value **3**.
        * Input: Press **'Y'** to move the cursor Left (decrease value).
        * Input: Press **'N'** to move the cursor Right (increase value).
        * Input: Press **'O'** to Confirm selection.

#### 3. Experiment Design & Randomization
* **Configuration:** The inspector should allow me to input a list of `NurbsSurface Amplitudes` and a list of `CD Ratios`.
* **Trial Generation:** On Start, generate the full list of trials by creating the Cartesian product of all Amplitudes and Ratios (every combination).
* **Repetition:** Each combination must be repeated $n$ times (where $n$ is an adjustable public integer).
* **Shuffle:** The entire list of trials (Combinations $\times$ Repetitions) must be randomized (shuffled) before the experiment begins.

#### 4. Data Logging
* **Initialization:** At the start, prompt for or generate a `Participant ID`.
* **Recording:** Save a CSV file containing:
    * Participant ID
    * Trial Number
    * Input parameters: Nurbs Amplitude, C/D Ratio
    * User Results: Answer to Q1 (Yes/No), Answer to Q2 (Confidence Level 1-5).

