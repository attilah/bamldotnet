class Recipe {
  title string
  ingredients string[]
  calories int?
}

enum Sentiment {
  Happy @description("User is happy")
  Sad @description("User is sad")
  Angry
}

function AnalyzeSentiment(text: string) -> Sentiment {
  client GPT4
  prompt #"
    Analyze the sentiment of: {{text}}
  "#
}

client<llm> GPT4 {
  provider openai
  retry_policy MyRetryPolicy
  options {
    model gpt-4
    api_key env.OPENAI_API_KEY
  }
}

retry_policy MyRetryPolicy {
  max_retries 3
  strategy {
    type exponential_backoff
    delay_ms 1000
    multiplier 2.0
    max_delay_ms 10000
  }
}

generator csharp_metadata {
  output_type csharp/metadata
  output_dir "../output"
  version "0.212.0"
}
