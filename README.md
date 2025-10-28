`dotnet DbConnectionTester.dll --urls "http://0.0.0.0:5000"`

```
#!/bin/bash

# --------------------------
# 1. Update and Upgrade OS
# --------------------------
sudo apt update -y && sudo apt upgrade -y

# --------------------------
# 2. Install Git and .NET SDK 8
# --------------------------
sudo apt install -y git wget
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt update -y
sudo apt install -y dotnet-sdk-8.0

# --------------------------
# 3. Clone Your Git Repo
# --------------------------
cd /home/ubuntu
if [ ! -d "DbTestApp" ]; then
  git clone https://github.com/Aravindh-29/DbTestApp.git
fi
cd DbTestApp

# --------------------------
# 4. Publish the App
# --------------------------
sudo dotnet publish -c Release -o /home/ubuntu/published

# --------------------------
# 5. Create systemd Service
# --------------------------
sudo tee /etc/systemd/system/dbconnection.service > /dev/null <<EOL
[Unit]
Description=DbConnectionTester .NET App
After=network.target

[Service]
WorkingDirectory=/home/ubuntu/published
ExecStart=/usr/bin/dotnet /home/ubuntu/published/DbConnectionTester.dll --urls "http://0.0.0.0:5000"
Restart=always
RestartSec=10
User=ubuntu
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
EOL

# Reload systemd, enable and start service
sudo systemctl daemon-reload
sudo systemctl enable dbconnection.service
sudo systemctl start dbconnection.service

echo "✅ Setup complete! App is running and accessible via http://<EC2-Public-IP>:5000"

```



# JenkinsFile 


```
pipeline {
    agent any

    environment {
        SONAR_TOKEN = credentials('Sonar-token')   // available in all stages
    }

    stages {
        stage('SCM') {
            steps {
                git('https://github.com/Aravindh-29/DbTestApp.git')
            }
        }

        stage('SonarQube Analysis') {
            steps {
                withSonarQubeEnv('SonarServer') {
                    sh """
                        export PATH=\$PATH:\$HOME/.dotnet/tools
                        
                        dotnet sonarscanner begin \
                          /k:"nowshad13" \
                          /o:"nowshad13" \
                          /d:sonar.host.url="https://sonarcloud.io" \
                          /d:sonar.login=\$SONAR_TOKEN

                        dotnet build -c Release

                        dotnet sonarscanner end /d:sonar.login=\$SONAR_TOKEN
                    """
                }
            }
        }

        stage('Build') {
            steps {
                sh 'dotnet build DbConnectionTester.sln'
            }
        }

        stage('Test') {
            steps {
                sh 'dotnet test DbConnectionTester.sln'
            }
        }

        stage('Publish') {
            steps {
                sh 'dotnet publish DbConnectionTester.sln -c Release -o ./published'
            }
        }

        stage('Archive Artifacts') {
            steps {
                archiveArtifacts artifacts: 'published/**', fingerprint: true
            }
        }
    }
}

```
## Line-by-line explanation (SONAR STAGE)


* Install Sonarscanner on yourmachine firstly, make sure dotnet sdk present before you download this.
```
dotnet tool install --global dotnet-sonarscanner
```

* `withSonarQubeEnv('SonarServer')`

  * Tells Jenkins to use the Sonar server config named **SonarServer** (you set this in *Manage Jenkins → Configure System → SonarQube servers*).
  * Also helps `waitForQualityGate` (if used) to work later.

* `sh """ ... """`

  * Runs a bash script on the agent. Triple quotes are Groovy multiline strings.

* `export PATH=\$PATH:\$HOME/.dotnet/tools`

  * Ensures `dotnet-sonarscanner` (a global dotnet tool) can be found.
  * **Why `\$PATH` and not `$PATH`?** — inside a Jenkinsfile multiline string, Groovy would try to expand `$PATH`. Backslash `\` prevents Groovy from touching it so the shell sees `$PATH`. (In plain terminal you use `$PATH`.)

* `dotnet sonarscanner begin \`

  * **Starts** the Sonar analysis session. The flags that follow configure the analysis.

  Flags:

  * `/k:"nowshad13"` → **project key** inside Sonar (unique id for this project). **No spaces** after `:` — must be `/k:"..."`.
  * `/o:"nowshad13"` → **organization** in SonarCloud **(only for SonarCloud)**. Remove this for self-hosted SonarQube.
  * `/d:sonar.host.url="https://sonarcloud.io"` → explicit host url. Important because newer scanners default to SonarCloud. For self-hosted SonarQube use `http://localhost:9000` (or your server URL).
  * `/d:sonar.login=\$SONAR_TOKEN` → authentication token. We pass the token from Jenkins credentials (escape `\$` so Groovy doesn't expand it).

* `dotnet build -c Release`

  * Builds the project in **Release** configuration. The Sonar scanner hooks into MSBuild during this build and collects analysis data.
  * **Important:** run this in the same folder where your `.sln` or `.csproj` is. You can specify solution explicitly: `dotnet build DbConnectionTester.sln -c Release`.

* `dotnet sonarscanner end /d:sonar.login=\$SONAR_TOKEN`

  * **Ends** the analysis session and uploads results to Sonar.

# Nexus Repository Manager

---

# 🔹 1. Overview of Nexus

Nexus Repository (by **Sonatype**) is a **repository manager**.
Think of it like a **storage hub** where you keep your build artifacts, Docker images, dependencies, and libraries.

👉 Simple gaa cheppali ante:

* GitHub = code repo
* Jenkins = build tool
* Nexus = artifacts repo (built files, jars, dlls, docker images, etc.)

---

## 🔹 2. Why we need Nexus?

1. **Central storage** → Store artifacts (JARs, WARs, DLLs, NuGet packages, Docker images).
2. **Dependency management** → Developers can download libraries from Nexus instead of internet.
3. **CI/CD integration** → Jenkins builds → uploads artifacts → Nexus stores them.
4. **Security** → You control which artifacts are allowed.
5. **Caching** → Speeds up builds by caching Maven, npm, NuGet, etc.

---

## 🔹 3. Types of repositories in Nexus

* **Hosted** → Store your own artifacts (e.g., JARs, Docker images, NuGet packages).
* **Proxy** → Cache remote repos (like Maven Central, npmjs, NuGet Gallery).
* **Group** → Combine multiple repos into one URL (easy for developers).

---

## 🔹 4. Nexus Editions

* **Nexus Repository OSS (Free)** → open source, supports many formats (Maven, npm, NuGet, Docker, etc.).
* **Nexus Pro (Paid / Free Trial)** → enterprise features like staging, advanced security, support.

👉 For learning/POC → **OSS free version is enough** (runs on your EC2/Docker).
👉 For trial → You can request Pro trial here:
🔗 [Sonatype Nexus Repository Free Trial](https://www.sonatype.com/products/sonatype-nexus-repository/free-trial)

---

## 🔹 5. How to run Nexus locally (free OSS)

Run Nexus in Docker (easiest way):

```bash
docker run -d -p 8081:8081 --name nexus sonatype/nexus3
```

* Access UI at → `http://<your-server-ip>:8081`
* Default user: `admin`
* Password: in container at `/nexus-data/admin.password`

---

## 🔹 6. How Nexus works with Jenkins

The typical CI/CD flow is:

1. **Build app** in Jenkins (`dotnet publish`, `mvn package`, `docker build`, etc.)
2. **Upload artifact** to Nexus repo using Jenkins.

   * Example: upload `.zip`, `.dll`, `.jar`, or Docker image.
3. **Later** → Developers or deployment scripts pull artifact from Nexus for deployment.

---

## 🔹 7. Jenkins Pipeline + Nexus Example

### Case 1: Upload a **ZIP/Build Artifact**

```groovy
stage('Upload to Nexus') {
    steps {
        nexusPublisher nexusInstanceId: 'nexus-server',
                       nexusRepositoryId: 'my-hosted-repo',
                       packages: [
                         [
                           $class: 'MavenPackage',
                           mavenAssetList: [
                             [classifier: '', extension: 'zip', filePath: 'published/myapp.zip']
                           ],
                           mavenCoordinate: [
                             artifactId: 'dbtestapp',
                             groupId: 'com.aravindh',
                             packaging: 'zip',
                             version: '1.0.0'
                           ]
                         ]
                       ]
    }
}
```

👉 Here:

* `nexusInstanceId` = the Nexus server config ID in Jenkins (set in **Manage Jenkins → Nexus Configuration**).
* `nexusRepositoryId` = repo name (e.g., `releases`, `snapshots`, or your own).
* `filePath` = artifact path (like `published/myapp.zip`).

---

### Case 2: Push a **Docker Image** to Nexus

If Nexus repo is configured as Docker registry:

```groovy
stage('Build & Push Docker') {
    steps {
        sh """
            docker build -t nexus-server:8082/dbtestapp:1.0.0 .
            docker login nexus-server:8082 -u admin -p $NEXUS_PASS
            docker push nexus-server:8082/dbtestapp:1.0.0
        """
    }
}
```

---

Got it ✅ Let me rewrite the full **Nexus Repository installation guide on Ubuntu** in a clean, step-by-step format:

---

# 🚀 Installing Nexus Repository on Ubuntu

## 1. Install Java (Nexus requires Java 8 / 11 / 17)

```bash
sudo apt update
sudo apt install openjdk-11-jdk -y
```

### Verify Java:

```bash
java -version
```

### Check all installed versions:

```bash
ls /usr/lib/jvm/
```

---

## 2. Create a Nexus User

It’s best practice to run Nexus under its own user.

```bash
sudo useradd -m nexus
sudo passwd nexus
```

Add Nexus user to sudo group:

```bash
sudo usermod -aG sudo nexus
```

---

## 3. Download Nexus from Official Site

Go to [Sonatype Download Page](https://help.sonatype.com/en/download.html?utm_source=chatgpt.com) and get the latest version.

Example (3.84.1-01):

```bash
cd /opt
sudo wget https://download.sonatype.com/nexus/3/nexus-3.84.1-01-linux-x86_64.tar.gz
```

---

## 4. Extract Nexus and Configure Permissions

```bash
sudo tar -xvzf nexus-3.84.1-01-linux-x86_64.tar.gz
sudo mv nexus-3.84.1-01 nexus
sudo chown -R nexus:nexus /opt/nexus
sudo chown -R nexus:nexus /opt/sonatype-work
```

---

## 5. Create a Systemd Service for Nexus

Create a service file:

```bash
sudo nano /etc/systemd/system/nexus.service
```

Paste this:

```ini
[Unit]
Description=Nexus Service
After=network.target

[Service]
Type=forking
LimitNOFILE=65536
ExecStart=/opt/nexus/bin/nexus start
ExecStop=/opt/nexus/bin/nexus stop
User=nexus
Restart=on-abort

[Install]
WantedBy=multi-user.target
```

Save and exit (`CTRL+O`, `CTRL+X`).

---

## 6. Enable and Start Nexus Service

```bash
sudo systemctl daemon-reload
sudo systemctl enable nexus
sudo systemctl start nexus
sudo systemctl status nexus
```

---

## 7. Access Nexus Web UI

Open in browser:

```
http://<your-server-ip>:8081
```

Get the admin password:

```bash
cat /opt/sonatype-work/nexus3/admin.password
```

Login with:

* **Username:** `admin`
* **Password:** (value from file above)

---

✅ Done! Nexus Repository is now installed and running as a service on Ubuntu.

# Nexus integrated with Jenkins Pipeline

```
pipeline{
    agent any
    environment{
        SONAR_TOKEN = credentials('SONAR_TOKEN')
        NEXUS_CRED = credentials('Nexus-Credentials') // username:password
    }
    stages{
        // 1️⃣ Checkout source code
        stage('SCM Checkout'){
            steps{
                git('https://github.com/Aravindh-29/DbTestApp.git')
            }
        }

        // 2️⃣ Static Code Analysis
        stage('SonarCloud Analysis'){
            steps{
                withSonarQubeEnv('SonarServer'){
                    sh '''
                        export PATH=$PATH:$HOME/.dotnet/tools

                        dotnet sonarscanner begin \
                          /k:"nowshad13" \
                          /o:"nowshad13" \
                          /d:"sonar.host.url=https://sonarcloud.io" \
                          /d:"sonar.login=$SONAR_TOKEN"

                        dotnet build DbConnectionTester.sln -c Release

                        dotnet sonarscanner end /d:"sonar.login=$SONAR_TOKEN"
                    '''
                }
            }
        }

        // 3️⃣ Build Solution
        stage('Build Solution'){
            steps{
                sh 'dotnet build DbConnectionTester.sln -c Release'
            }
        }

        // 4️⃣ Publish Artifacts
        stage('Publish Artifacts'){
            steps{
                sh '''
                    dotnet publish DbConnectionTester.sln -c Release -o ./published
                    ls -l published
                '''
                archiveArtifacts artifacts: 'published/**', fingerprint: true
            }
        }

        // 5️⃣ Push Artifacts to Nexus
        stage('Push to Nexus'){
            steps{
                sh '''
                    for file in published/*; do
                        curl -u $NEXUS_CRED --upload-file "$file" \
                            http://13.233.196.37:8081/repository/dotnet-artifacts/$(basename $file)
                    done
                '''
            }
        }
    }
}

```
Sure Aravindh! Let me give you a **complete step-by-step recap** for connecting Jenkins/EC2 to AWS ECR, so you can use it as reference in future. I’ll keep it clear and simple.

---

# **AWS ECR Connection Setup – Full Steps**

---

## **1️⃣ Pre-requisites**

* EC2 instance running Ubuntu
* Docker installed
* IAM Role attached to EC2 with **ECR full access** (`AmazonEC2ContainerRegistryFullAccess`)
* Jenkins installed (if using pipeline)
* AWS CLI v2 installed

---

## **2️⃣ Install Docker (if not done yet)**

```bash
sudo apt update
sudo apt install -y docker.io
sudo systemctl enable docker
sudo systemctl start docker

# Add Jenkins user to docker group
sudo usermod -aG docker jenkins
newgrp docker  # apply new group immediately
```

Check Docker:

```bash
docker --version
```

---

## **3️⃣ Install AWS CLI v2**

```bash
curl "https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip" -o "awscliv2.zip"
unzip awscliv2.zip
sudo ./aws/install
aws --version
```

---

## **4️⃣ Attach IAM Role to EC2**

* Attach **role with ECR permissions** to EC2 instance.
* Verify role is attached:

```bash
TOKEN=$(curl -X PUT "http://169.254.169.254/latest/api/token" -H "X-aws-ec2-metadata-token-ttl-seconds: 21600")
curl -H "X-aws-ec2-metadata-token: $TOKEN" http://169.254.169.254/latest/meta-data/iam/security-credentials/
```

You should see the **role name**.

---

## **5️⃣ Create ECR repository (UI Steps)**

1. Go to AWS Console → **ECR** → **Repositories**
2. Click **Create repository**

   * Repository name: e.g., `my-app-repo`
   * Visibility: Private
   * Leave other options default
3. Click **Create repository**

> You will see `Repository URI` like:
> `669443521868.dkr.ecr.ap-south-1.amazonaws.com/my-app-repo`

---

## **6️⃣ Authenticate Docker to ECR**

```bash
aws ecr get-login-password --region ap-south-1 | \
docker login --username AWS --password-stdin 669443521868.dkr.ecr.ap-south-1.amazonaws.com
```

* If login succeeds, you can push images to ECR.

---

## **7️⃣ Build Docker Image**

```bash
# In project folder
docker build -t dbconnectiontester .
```

---

## **8️⃣ Tag Docker Image for ECR**

```bash
docker tag dbconnectiontester:latest 669443521868.dkr.ecr.ap-south-1.amazonaws.com/my-app-repo:latest

# Optional: tag with commit hash / version
docker tag dbconnectiontester:latest 669443521868.dkr.ecr.ap-south-1.amazonaws.com/my-app-repo:<commit-hash>
```

---

## **9️⃣ Push Docker Image to ECR**

```bash
docker push 669443521868.dkr.ecr.ap-south-1.amazonaws.com/my-app-repo:latest
docker push 669443521868.dkr.ecr.ap-south-1.amazonaws.com/my-app-repo:<commit-hash>
```

---

## **10️⃣ Verify Image in ECR**

* Go to **AWS ECR → Repositories → my-app-repo → Images**
* You will see `latest` and versioned images listed

---

✅ **Summary**

1. Install Docker & AWS CLI on EC2
2. Attach IAM role with ECR access
3. Create ECR repository in AWS console
4. Authenticate Docker with ECR (`aws ecr get-login-password`)
5. Build, tag, and push Docker images to ECR
6. Use versioned tags (commit hash) for rollback in production

---

If you want, I can also **write a ready-made Jenkins pipeline snippet** just for **ECR push** that you can reuse anytime.

Do you want me to do that?

