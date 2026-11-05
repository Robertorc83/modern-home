#!/bin/bash
# Runs inside LocalStack on first boot (mounted at /etc/localstack/init/ready.d).
# Creates everything the API and Worker expect. Idempotent.
set -euo pipefail
export AWS_DEFAULT_REGION="${AWS_DEFAULT_REGION:-us-east-1}"

BUCKET="${PHOTO_BUCKET:-listing-photos}"
QUEUE="${NOTIFICATIONS_QUEUE:-showing-notifications}"
SENDER="${SENDER_EMAIL:-no-reply@showings.local}"

echo "init-aws: bucket=$BUCKET queue=$QUEUE sender=$SENDER"

# S3 bucket for listing photos, with CORS so the browser can PUT to presigned URLs.
awslocal s3api head-bucket --bucket "$BUCKET" 2>/dev/null || awslocal s3 mb "s3://$BUCKET"
awslocal s3api put-bucket-cors --bucket "$BUCKET" --cors-configuration '{
  "CORSRules": [{
    "AllowedOrigins": ["*"],
    "AllowedMethods": ["GET", "PUT", "HEAD"],
    "AllowedHeaders": ["*"],
    "ExposeHeaders": ["ETag"],
    "MaxAgeSeconds": 3000
  }]
}'

# Dead letter queue first, then the main queue pointing at it (3 tries before a message is parked).
DLQ_URL=$(awslocal sqs create-queue --queue-name "$QUEUE-dlq" --query QueueUrl --output text)
DLQ_ARN=$(awslocal sqs get-queue-attributes --queue-url "$DLQ_URL" --attribute-names QueueArn --query Attributes.QueueArn --output text)
awslocal sqs create-queue --queue-name "$QUEUE" --attributes "{
  \"VisibilityTimeout\": \"30\",
  \"RedrivePolicy\": \"{\\\"deadLetterTargetArn\\\":\\\"$DLQ_ARN\\\",\\\"maxReceiveCount\\\":\\\"3\\\"}\"
}" >/dev/null

# SES sender identity (LocalStack accepts it immediately; real SES sends a verification email).
awslocal ses verify-email-identity --email-address "$SENDER"

echo "init-aws: done"
