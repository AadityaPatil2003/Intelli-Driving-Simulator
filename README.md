# Intelli-Driving-Simulator

> **Mixed-reality driver adaptation for a new road.**

## Overview

**Intelli-Driving-Simulator** is a Mixed Reality driver-training project developed for **COSC3140 Mixed Reality** by **Team VrOoOm**.

The project is designed for experienced drivers who move between countries with different road systems, traffic directions, vehicle layouts, and driving conventions.

Instead of teaching driving from the beginning, the simulator focuses on identifying **existing driving habits that may not transfer correctly to a new country**.

Examples include:

- checking the wrong direction first;
- drifting toward the familiar side of the road;
- using the wrong mirror;
- adapting to different vehicle layouts;
- responding incorrectly at intersections or roundabouts;
- carrying over habits from a previous road environment.

The aim is to provide a safe Mixed Reality environment where these habits can be observed, measured, and improved before the driver encounters similar situations in real traffic.

---

## How It Works

A typical training session follows this flow:

```text
Headset On
    ↓
Country Profile Selection
    ↓
Steering-Wheel / Prop Calibration
    ↓
Guided Drive
    ↓
Driver Telemetry Collection
    ↓
Hazard Scenario
    ↓
Performance Analysis
    ↓
Post-Drive Feedback
    ↓
Adaptive Next Training Session
```

The post-drive report is used to identify the driver's weakest areas and influence what they practise next.

---

## Why Mixed Reality?

The project uses **Mixed Reality rather than full Virtual Reality**.

This allows the user to remain aware of their real surroundings while interacting with virtual driving content.

The user can see real elements such as:

- their hands;
- the physical steering-wheel prop;
- the desk or supporting setup;

while virtual elements such as the following are added:

- road environment;
- traffic;
- lane markings;
- road signs;
- mirrors;
- speed information;
- hazard scenarios;
- feedback.

This approach reduces the need to build a complete virtual car interior while still providing physical interaction.

---

## Main Features

The planned system includes:

- Meta Quest 3 Mixed Reality passthrough;
- hand tracking;
- physical steering-wheel interaction;
- a virtual urban driving environment;
- multiple country-driving profiles;
- left-side and right-side traffic configurations;
- vehicle control using Unity WheelCollider;
- driver telemetry collection;
- driver-performance analysis;
- rule-based risk scoring;
- adaptive training scenarios;
- post-drive feedback;
- three hazard scenarios:
  - pedestrian step-out;
  - cyclist in blind spot;
  - sudden lead-vehicle braking.

---

## Driver Behaviour Analysis

The simulator is designed to record and analyse driving behaviour using telemetry.

The core planned metrics are:

- `mirror_check_freq`
- `lane_offset_rms`
- `steering_jerk`
- `gap_acceptance`

These measurements contribute to a driver-performance profile and an overall risk score.

The system can then recommend the next training session based on the driver's weakest area.

---

## Country Adaptation

One of the key ideas of Intelli-Driving-Simulator is to support multiple driving environments using a **single codebase**.

The project is designed around country profiles such as:

- Australia / Victoria
- India
- USA / UAE-style right-side traffic

These profiles can control differences such as:

- traffic side;
- driver/cabin side;
- mirror priority;
- roundabout rules;
- turn-on-red rules;
- lane discipline;
- country-specific road behaviour.

---

## Technology

The project is being developed using:

- **Unity 6**
- **OpenXR**
- **XR Interaction Toolkit**
- **Meta XR SDK**
- **Meta Quest 3**
- **Unity WheelCollider**
- **C#**
- **GitHub**

Driver-behaviour modelling and later classifier experiments may also use:

- **Python**
- **pandas**
- **NumPy**
- **scikit-learn**

---

# Team VrOoOm

## Aaditya Patil  
### Project Manager & XR Integration

Aaditya is responsible for the overall technical integration and project coordination.

His responsibilities include:

- project timeline and sprint coordination;
- Unity XR setup;
- OpenXR configuration;
- XR Interaction Toolkit integration;
- Meta Quest 3 integration;
- passthrough setup;
- headset input;
- hand tracking;
- steering-wheel calibration;
- GitHub/repository coordination;
- final system integration;
- Quest builds and APK packaging.

---

## Ananya Rohatgi  
### Vehicle & Environment

Ananya is responsible for the driving system and virtual road environment.

Her responsibilities include:

- vehicle control;
- Unity WheelCollider configuration;
- steering, acceleration, and braking;
- road and lane design;
- intersections;
- signage;
- lane markings;
- waypoint traffic;
- NPC vehicles;
- collision detection;
- left-side and right-side traffic configuration;
- country-based environment changes.

---

## Anushka Kamble  
### AI & Driver Behaviour

Anushka is responsible for analysing driver behaviour and creating the adaptive intelligence system.

Her responsibilities include:

- telemetry collection;
- driver-performance metrics;
- feature extraction;
- country-rule configuration;
- rule-based risk scoring;
- driver profiles;
- adaptive scenario selection;
- performance analysis;
- classifier development using logged driving sessions.

---

## Omkar Rakesh Singh  
### Scenarios, UI & Evaluation

Omkar is responsible for the hazard scenarios, user interface, and evaluation of the final system.

His responsibilities include:

- pedestrian step-out scenario;
- cyclist blind-spot scenario;
- sudden lead-vehicle braking scenario;
- live driver alerts;
- driving HUD;
- post-drive dashboard;
- performance feedback;
- scenario testing;
- user-testing design;
- user evaluation;
- presentation and demonstration media.

---

## Project Goal

The final goal of Intelli-Driving-Simulator is to create a Mixed Reality training experience that can:

1. observe driver behaviour;
2. identify habits that may not transfer correctly between countries;
3. safely expose the driver to relevant hazards;
4. provide understandable feedback;
5. adapt future training based on the driver's performance.

---

## Team

**Team VrOoOm**

- Aaditya Patil — Project Manager & XR Integration
- Ananya Rohatgi — Vehicle & Environment
- Anushka Kamble — AI & Driver Behaviour
- Omkar Rakesh Singh — Scenarios, UI & Evaluation
